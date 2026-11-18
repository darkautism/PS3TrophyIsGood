using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace PS3TrophyIsGood
{
    /// <summary>
    /// Minimal, dependency-free reader for .xlsx/.xlsm workbooks (first worksheet) and .csv/.tsv/.txt files.
    /// Every cell is returned as a SpreadsheetCell holding either text or a raw number, so that
    /// Excel date serials can be told apart from text dates.
    /// </summary>
    internal static class SpreadsheetReader
    {
        internal class SpreadsheetCell
        {
            public string Text = string.Empty;
            public double? Number;

            public bool IsEmpty
            {
                get { return !Number.HasValue && string.IsNullOrWhiteSpace(Text); }
            }

            public override string ToString()
            {
                return Number.HasValue ? Number.Value.ToString(CultureInfo.InvariantCulture) : Text;
            }
        }

        public static List<List<SpreadsheetCell>> Read(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".xlsx":
                case ".xlsm":
                    return ReadXlsx(path);
                case ".csv":
                case ".tsv":
                case ".txt":
                    return ReadCsv(path);
                case ".xls":
                    throw new NotSupportedException(
                        "Old-style .xls files are not supported. In Excel, use File > Save As and choose " +
                        "\"Excel Workbook (*.xlsx)\" or \"CSV UTF-8 (*.csv)\", then import that file.");
                default:
                    throw new NotSupportedException("Unsupported file type: " + ext + ". Use .xlsx or .csv.");
            }
        }

        // Opens the file even while Excel has it open.
        private static FileStream OpenShared(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        #region XLSX

        private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        private static List<List<SpreadsheetCell>> ReadXlsx(string path)
        {
            using (FileStream fs = OpenShared(path))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                List<string> sharedStrings = ReadSharedStrings(zip);
                string sheetPath = FindFirstSheetPath(zip);
                ZipArchiveEntry sheetEntry = GetEntry(zip, sheetPath);
                if (sheetEntry == null)
                    throw new InvalidDataException("Could not find the first worksheet in the workbook.");

                XDocument sheet;
                using (Stream s = sheetEntry.Open())
                    sheet = XDocument.Load(s);

                List<List<SpreadsheetCell>> rows = new List<List<SpreadsheetCell>>();
                XElement sheetData = sheet.Root == null ? null : sheet.Root.Element(MainNs + "sheetData");
                if (sheetData == null)
                    return rows;

                int nextRowIndex = 0;
                foreach (XElement rowEl in sheetData.Elements(MainNs + "row"))
                {
                    int rowIndex = nextRowIndex;
                    XAttribute rAttr = rowEl.Attribute("r");
                    int parsedRow;
                    if (rAttr != null && int.TryParse(rAttr.Value, out parsedRow) && parsedRow > 0)
                        rowIndex = parsedRow - 1;
                    nextRowIndex = rowIndex + 1;

                    while (rows.Count <= rowIndex)
                        rows.Add(new List<SpreadsheetCell>());
                    List<SpreadsheetCell> row = rows[rowIndex];

                    int nextCol = 0;
                    foreach (XElement c in rowEl.Elements(MainNs + "c"))
                    {
                        int col = nextCol;
                        XAttribute cr = c.Attribute("r");
                        if (cr != null)
                        {
                            int parsedCol = ColumnIndexFromReference(cr.Value);
                            if (parsedCol >= 0)
                                col = parsedCol;
                        }
                        nextCol = col + 1;

                        while (row.Count <= col)
                            row.Add(new SpreadsheetCell());
                        row[col] = ReadCell(c, sharedStrings);
                    }
                }
                return rows;
            }
        }

        private static SpreadsheetCell ReadCell(XElement c, List<string> sharedStrings)
        {
            SpreadsheetCell cell = new SpreadsheetCell();
            string type = (string)c.Attribute("t") ?? "n";
            XElement v = c.Element(MainNs + "v");

            switch (type)
            {
                case "s":
                    int idx;
                    if (v != null && int.TryParse(v.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)
                        && idx >= 0 && idx < sharedStrings.Count)
                        cell.Text = sharedStrings[idx];
                    break;
                case "inlineStr":
                    XElement isEl = c.Element(MainNs + "is");
                    cell.Text = isEl == null ? string.Empty : JoinText(isEl);
                    break;
                case "str":
                case "d":
                    cell.Text = v == null ? string.Empty : v.Value;
                    break;
                case "b":
                case "e":
                    cell.Text = v == null ? string.Empty : v.Value;
                    break;
                default: // "n"
                    if (v != null)
                    {
                        double d;
                        if (double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                            cell.Number = d;
                        else
                            cell.Text = v.Value;
                    }
                    break;
            }
            return cell;
        }

        private static string JoinText(XElement container)
        {
            // Rich text is split into <r><t/></r> runs; phonetic hints (<rPh>) are ignored.
            StringBuilder sb = new StringBuilder();
            foreach (XElement t in container.Descendants(MainNs + "t"))
            {
                if (t.Ancestors(MainNs + "rPh").Any())
                    continue;
                sb.Append(t.Value);
            }
            return sb.ToString();
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            List<string> result = new List<string>();
            ZipArchiveEntry entry = GetEntry(zip, "xl/sharedStrings.xml");
            if (entry == null)
                return result;
            XDocument doc;
            using (Stream s = entry.Open())
                doc = XDocument.Load(s);
            if (doc.Root == null)
                return result;
            foreach (XElement si in doc.Root.Elements(MainNs + "si"))
                result.Add(JoinText(si));
            return result;
        }

        private static string FindFirstSheetPath(ZipArchive zip)
        {
            const string fallback = "xl/worksheets/sheet1.xml";
            ZipArchiveEntry wbEntry = GetEntry(zip, "xl/workbook.xml");
            ZipArchiveEntry relEntry = GetEntry(zip, "xl/_rels/workbook.xml.rels");
            if (wbEntry == null || relEntry == null)
                return fallback;

            XDocument wb, rels;
            using (Stream s = wbEntry.Open()) wb = XDocument.Load(s);
            using (Stream s = relEntry.Open()) rels = XDocument.Load(s);

            XElement firstSheet = wb.Descendants(MainNs + "sheet").FirstOrDefault();
            if (firstSheet == null)
                return fallback;
            string relId = (string)firstSheet.Attribute(RelNs + "id");
            XElement rel = rels.Descendants(PkgRelNs + "Relationship")
                .FirstOrDefault(r => (string)r.Attribute("Id") == relId);
            if (rel == null)
                return fallback;

            string target = ((string)rel.Attribute("Target") ?? string.Empty).Replace('\\', '/');
            if (target.StartsWith("/"))
                return target.TrimStart('/');
            return "xl/" + target;
        }

        private static ZipArchiveEntry GetEntry(ZipArchive zip, string name)
        {
            return zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase));
        }

        private static int ColumnIndexFromReference(string reference)
        {
            int col = 0;
            int letters = 0;
            foreach (char ch in reference)
            {
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
                else break;
                letters++;
            }
            return letters == 0 ? -1 : col - 1;
        }

        #endregion

        #region CSV

        private static List<List<SpreadsheetCell>> ReadCsv(string path)
        {
            string text;
            using (FileStream fs = OpenShared(path))
            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true))
                text = sr.ReadToEnd();

            char delimiter = DetectDelimiter(text);
            List<List<SpreadsheetCell>> rows = new List<List<SpreadsheetCell>>();
            List<SpreadsheetCell> row = new List<SpreadsheetCell>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(ch);
                }
                else if (ch == '"') inQuotes = true;
                else if (ch == delimiter)
                {
                    row.Add(new SpreadsheetCell { Text = field.ToString() });
                    field.Clear();
                }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(new SpreadsheetCell { Text = field.ToString() });
                    field.Clear();
                    rows.Add(row);
                    row = new List<SpreadsheetCell>();
                }
                else field.Append(ch);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(new SpreadsheetCell { Text = field.ToString() });
                rows.Add(row);
            }
            return rows;
        }

        private static char DetectDelimiter(string text)
        {
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            string firstLine = end < 0 ? text : text.Substring(0, end);
            char[] candidates = { ',', ';', '\t', '|' };
            char best = ',';
            int bestCount = 0;
            foreach (char c in candidates)
            {
                int count = firstLine.Count(ch => ch == c);
                if (count > bestCount) { best = c; bestCount = count; }
            }
            return best;
        }

        #endregion
    }
}
