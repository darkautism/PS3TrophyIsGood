using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using TROPHYParser;
using Cell = PS3TrophyIsGood.SpreadsheetReader.SpreadsheetCell;

namespace PS3TrophyIsGood
{
    /// <summary>
    /// Adds "Export trophy list (CSV)..." and "Import times from Excel/CSV..." to the Advanced menu.
    ///
    /// Import rules:
    ///  * Reads the first worksheet of an .xlsx/.xlsm file, or a .csv/.tsv/.txt file.
    ///  * A header row is detected automatically (ID / Name / Date-Time columns). Without a header,
    ///    column A is the trophy ID (or name) and the first column holding dates is the unlock time.
    ///  * Rows are matched to trophies by ID when an ID is present, otherwise by exact trophy name.
    ///  * Rows with an empty time cell are ignored, so those trophies are left unchanged.
    ///  * Synced trophies, times before the last PSN sync and dates before 2008 are skipped.
    ///  * The platinum is applied last and only when every base-game trophy is unlocked.
    ///  * Nothing is written until the preview is confirmed, and the file still has to be saved
    ///    with File > Save afterwards, exactly like a manual edit.
    /// </summary>
    public partial class MainAPP
    {
        private const string ImportTitle = "Import from Excel/CSV";
        private const string ExportTimeFormat = "yyyy-MM-dd HH:mm:ss";

        private ToolStripMenuItem exportTrophyListMenuItem;
        private ToolStripMenuItem importSpreadsheetMenuItem;

        private void InitializeSpreadsheetMenu()
        {
            exportTrophyListMenuItem = new ToolStripMenuItem("Export trophy list (CSV)...");
            exportTrophyListMenuItem.Name = "exportTrophyListMenuItem";
            exportTrophyListMenuItem.Click += exportTrophyListMenuItem_Click;

            importSpreadsheetMenuItem = new ToolStripMenuItem("Import times from Excel/CSV...");
            importSpreadsheetMenuItem.Name = "importSpreadsheetMenuItem";
            importSpreadsheetMenuItem.Click += importSpreadsheetMenuItem_Click;

            int insertAt = 進階ToolStripMenuItem.DropDownItems.IndexOf(toolStripMenuItem1) + 1;
            if (insertAt <= 0)
                insertAt = 進階ToolStripMenuItem.DropDownItems.Count;
            進階ToolStripMenuItem.DropDownItems.Insert(insertAt, importSpreadsheetMenuItem);
            進階ToolStripMenuItem.DropDownItems.Insert(insertAt, exportTrophyListMenuItem);
        }

        private bool EnsureTrophyFileOpen()
        {
            if (tconf == null || tusr == null || tpsn == null)
            {
                MessageBox.Show("Open a trophy folder first.", ImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (listViewEx1.IsEditing)
                listViewEx1.EndEditing(true);
            return true;
        }

        #region Export

        private void exportTrophyListMenuItem_Click(object sender, EventArgs e)
        {
            if (!EnsureTrophyFileOpen())
                return;

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Export trophy list";
                dlg.Filter = "CSV file (*.csv)|*.csv";
                dlg.FileName = MakeSafeFileName((tconf.title_name ?? "trophies") + " - " + (tconf.npcommid ?? "")) + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("ID,Name,Description,Type,Group,Unlocked Time");
                for (int i = 0; i < tconf.Count; i++)
                {
                    TROPCONF.Trophy t = tconf[i];
                    string time = string.Empty;
                    if (IsTrophyGot(t.id))
                    {
                        DateTime dt = tpsn[t.id].HasValue ? tpsn[t.id].Value.Time : tusr.trophyTimeInfoTable[t.id].Time;
                        time = dt.ToString(ExportTimeFormat, CultureInfo.InvariantCulture);
                    }
                    sb.Append(t.id.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(CsvEscape(t.name)).Append(',')
                      .Append(CsvEscape(t.detail)).Append(',')
                      .Append(CsvEscape(TrophyTypeName(t.ttype))).Append(',')
                      .Append(t.gid == 0 ? "Base" : "DLC" + t.gid).Append(',')
                      .Append(time).AppendLine();
                }

                try
                {
                    // UTF-8 with BOM so Excel shows accented/Japanese/Chinese names correctly.
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                    MessageBox.Show(
                        "Exported " + tconf.Count + " trophies.\n\nFill in the \"Unlocked Time\" column " +
                        "(leave a cell empty to keep that trophy unchanged), then use " +
                        "Advanced > Import times from Excel/CSV.\n\nTip: saving as .xlsx keeps the seconds; " +
                        "Excel may drop seconds when it re-saves a .csv.",
                        "Export trophy list", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Export trophy list", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string TrophyTypeName(string ttype)
        {
            switch ((ttype ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "P": return "Platinum";
                case "G": return "Gold";
                case "S": return "Silver";
                case "B": return "Bronze";
                default: return ttype ?? string.Empty;
            }
        }

        private static string CsvEscape(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n', ';' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim().TrimEnd('-', ' ');
        }

        #endregion

        #region Import

        private class PlannedTime
        {
            public int Id;
            public DateTime Time;
            public int Row; // 1-based spreadsheet row, for messages
        }

        private void importSpreadsheetMenuItem_Click(object sender, EventArgs e)
        {
            if (!EnsureTrophyFileOpen())
                return;

            string fileName;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = ImportTitle;
                dlg.Filter = "Spreadsheets (*.xlsx;*.xlsm;*.csv;*.tsv;*.txt)|*.xlsx;*.xlsm;*.csv;*.tsv;*.txt|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                fileName = dlg.FileName;
            }

            List<List<Cell>> rows;
            try
            {
                rows = SpreadsheetReader.Read(fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read the file:\n" + ex.Message, ImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            List<string> issues = new List<string>();
            List<PlannedTime> plan = BuildImportPlan(rows, issues);
            if (plan == null)
                return; // BuildImportPlan already explained why

            if (plan.Count == 0)
            {
                MessageBox.Show("No trophy times could be imported. Nothing was changed." + FormatIssues(issues),
                    ImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StringBuilder preview = new StringBuilder();
            preview.AppendLine(plan.Count + " trophies will be set from \"" + Path.GetFileName(fileName) + "\":");
            preview.AppendLine();
            const int maxPreview = 20;
            foreach (PlannedTime p in plan.OrderBy(x => x.Id).Take(maxPreview))
                preview.AppendLine("  #" + p.Id + "  " + Shorten(tconf[p.Id].name, 40) + "  →  " +
                                   p.Time.ToString(Properties.strings.DateFormatString));
            if (plan.Count > maxPreview)
                preview.AppendLine("  ...and " + (plan.Count - maxPreview) + " more");
            preview.Append(FormatIssues(issues));
            preview.AppendLine();
            preview.AppendLine();
            preview.Append("Apply these changes? (You still need to use File > Save afterwards.)");

            if (MessageBox.Show(preview.ToString(), ImportTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            ApplyImportPlan(plan);
        }

        private List<PlannedTime> BuildImportPlan(List<List<Cell>> rows, List<string> issues)
        {
            // ---- locate columns ----
            int headerRow = -1, idCol = -1, nameCol = -1, dateCol = -1, timeCol = -1;
            for (int r = 0; r < Math.Min(rows.Count, 20) && headerRow < 0; r++)
            {
                int id = -1, name = -1, date = -1, dateOnly = -1, timeOnly = -1;
                for (int c = 0; c < rows[r].Count; c++)
                {
                    string raw = rows[r][c].Text.Trim();
                    string h = NormalizeHeader(raw);
                    if (h.Length == 0 && raw != "#") continue;

                    if (id < 0 && (raw == "#" || h == "id" || h == "trophyid" || h == "no" || h == "number" || h == "index"))
                        id = c;
                    else if (name < 0 && (h == "name" || h == "trophy" || h == "trophyname" || h == "title" || h == "trophytitle"))
                        name = c;
                    else if (h == "date")
                        dateOnly = c;
                    else if (h == "time")
                        timeOnly = c;
                    else if (date < 0 && (h.Contains("date") || h.Contains("time") || h.Contains("unlock") ||
                                          h.Contains("earn") || h.Contains("obtain")))
                        date = c;
                }

                bool splitDateAndTime = false;
                if (date < 0 && dateOnly >= 0) { date = dateOnly; splitDateAndTime = timeOnly >= 0; }
                else if (date < 0 && timeOnly >= 0) { date = timeOnly; }

                if (date >= 0 && (id >= 0 || name >= 0))
                {
                    headerRow = r; idCol = id; nameCol = name; dateCol = date;
                    // Separate "Date" and "Time" columns get combined.
                    if (splitDateAndTime) timeCol = timeOnly;
                }
            }

            if (headerRow < 0)
            {
                // No header: column A identifies the trophy, the first later column that looks like dates holds the time.
                idCol = 0;
                nameCol = 0;
                int best = -1, bestHits = 0;
                int width = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
                for (int c = 1; c < width; c++)
                {
                    int hits = 0;
                    foreach (List<Cell> row in rows)
                    {
                        DateTime tmp; string err;
                        if (c < row.Count && !row[c].IsEmpty && TryParseCellDate(row[c], null, out tmp, out err))
                            hits++;
                    }
                    if (hits > bestHits) { best = c; bestHits = hits; }
                }
                if (best < 0)
                {
                    MessageBox.Show(
                        "Couldn't find the trophy times in this sheet.\n\n" +
                        "Add a header row with an \"ID\" (or \"Name\") column and a \"Time\" column, " +
                        "or use Advanced > Export trophy list (CSV) to get a ready-made template.",
                        ImportTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                dateCol = best;
            }

            // ---- name lookup ----
            Dictionary<string, List<int>> byName = new Dictionary<string, List<int>>();
            for (int i = 0; i < tconf.Count; i++)
            {
                string key = NormalizeName(tconf[i].name);
                List<int> ids;
                if (!byName.TryGetValue(key, out ids)) { ids = new List<int>(); byName[key] = ids; }
                ids.Add(tconf[i].id);
            }

            // ---- read rows ----
            Dictionary<int, PlannedTime> planned = new Dictionary<int, PlannedTime>();
            DateTime minimumDate = new DateTime(MINIMUM_POSSIBLE_DATE);
            int trophyCount = tusr.trophyTimeInfoTable.Count;

            for (int r = headerRow + 1; r < rows.Count; r++)
            {
                List<Cell> row = rows[r];
                int displayRow = r + 1;
                Cell idCell = Get(row, idCol);
                Cell nameCell = Get(row, nameCol);
                Cell dateCell = Get(row, dateCol);
                Cell timeCell = timeCol >= 0 ? Get(row, timeCol) : null;

                if (dateCell.IsEmpty)
                    continue; // empty time = leave this trophy alone
                if (idCell.IsEmpty && nameCell.IsEmpty)
                    continue;

                // Which trophy?
                int trophyId = -1;
                int parsedId;
                if (!idCell.IsEmpty && TryGetInteger(idCell, out parsedId))
                {
                    if (parsedId < 0 || parsedId >= trophyCount)
                    {
                        issues.Add("Row " + displayRow + ": trophy ID " + parsedId + " doesn't exist in this game.");
                        continue;
                    }
                    trophyId = parsedId;

                    // Safety net: if the row also has a name, it must be this trophy's name.
                    if (nameCol >= 0 && nameCol != idCol && !nameCell.IsEmpty &&
                        NormalizeName(nameCell.ToString()) != NormalizeName(tconf[trophyId].name))
                    {
                        issues.Add("Row " + displayRow + ": ID " + trophyId + " is \"" + Shorten(tconf[trophyId].name, 30) +
                                   "\" but the row says \"" + Shorten(nameCell.ToString(), 30) + "\" - skipped.");
                        continue;
                    }
                }
                else
                {
                    string label = !nameCell.IsEmpty ? nameCell.ToString() : idCell.ToString();
                    List<int> ids;
                    if (!byName.TryGetValue(NormalizeName(label), out ids))
                    {
                        issues.Add("Row " + displayRow + ": no trophy named \"" + Shorten(label, 40) + "\".");
                        continue;
                    }
                    if (ids.Count > 1)
                    {
                        issues.Add("Row " + displayRow + ": more than one trophy is named \"" + Shorten(label, 40) + "\" - use its ID instead.");
                        continue;
                    }
                    trophyId = ids[0];
                }

                // When?
                DateTime time;
                string error;
                if (!TryParseCellDate(dateCell, timeCell, out time, out error))
                {
                    issues.Add("Row " + displayRow + ": " + error);
                    continue;
                }
                if (time < minimumDate)
                {
                    issues.Add("Row " + displayRow + ": " + time.ToString(ExportTimeFormat, CultureInfo.InvariantCulture) + " is before trophies existed on PS3.");
                    continue;
                }
                if (IsTrophySync(trophyId))
                {
                    issues.Add("#" + trophyId + " " + Shorten(tconf[trophyId].name, 30) + " is already synced with PSN and can't be changed.");
                    continue;
                }
                if (DateTime.Compare(lastSyncTrophyTime, time) > 0)
                {
                    issues.Add("Row " + displayRow + ": " + time.ToString(Properties.strings.DateFormatString) +
                               " is earlier than the last PSN sync (" + lastSyncTrophyTime.ToString(Properties.strings.DateFormatString) + ").");
                    continue;
                }
                if (time > DateTime.Now.AddDays(1))
                    issues.Add("Row " + displayRow + ": " + time.ToString(Properties.strings.DateFormatString) + " is in the future (imported anyway).");

                if (planned.ContainsKey(trophyId))
                    issues.Add("Row " + displayRow + ": trophy #" + trophyId + " appears more than once - the last row wins.");
                planned[trophyId] = new PlannedTime { Id = trophyId, Time = time, Row = displayRow };
            }

            // ---- platinum rule ----
            if (tconf.HasPlatinium && planned.ContainsKey(0))
            {
                int baseGot = 0;
                for (int i = 0; i < tconf.Count; i++)
                    if (tconf[i].gid == 0 && i != 0 && (IsTrophyGot(i) || planned.ContainsKey(i)))
                        baseGot++;
                if (baseGot < baseGamaCount)
                {
                    issues.Add("Platinum skipped: not every base-game trophy would be unlocked.");
                    planned.Remove(0);
                }
                else
                {
                    DateTime latestBase = DateTime.MinValue;
                    for (int i = 1; i < tconf.Count; i++)
                    {
                        if (tconf[i].gid != 0) continue;
                        PlannedTime p;
                        DateTime t;
                        if (planned.TryGetValue(i, out p)) t = p.Time;
                        else if (tpsn[i].HasValue) t = tpsn[i].Value.Time;
                        else if (tusr.trophyTimeInfoTable[i].IsGet) t = tusr.trophyTimeInfoTable[i].Time;
                        else continue;
                        if (t > latestBase) latestBase = t;
                    }
                    if (planned[0].Time < latestBase)
                        issues.Add("Note: the platinum time is earlier than the last base-game trophy (" +
                                   latestBase.ToString(Properties.strings.DateFormatString) + "), which looks odd on PSN.");
                }
            }

            return planned.Values.ToList();
        }

        private void ApplyImportPlan(List<PlannedTime> plan)
        {
            int applied = 0;
            List<string> failures = new List<string>();

            // Base/DLC trophies in time order, platinum last (it needs the others unlocked first).
            IEnumerable<PlannedTime> ordered = plan
                .OrderBy(p => (tconf.HasPlatinium && p.Id == 0) ? 1 : 0)
                .ThenBy(p => p.Time);

            foreach (PlannedTime p in ordered)
            {
                if (tconf.HasPlatinium && p.Id == 0 &&
                    Enumerable.Range(1, tconf.Count - 1).Count(i => tconf[i].gid == 0 && IsTrophyGot(i)) < baseGamaCount)
                {
                    failures.Add("Platinum skipped: not every base-game trophy is unlocked.");
                    continue;
                }

                bool hadOld = IsTrophyGot(p.Id);
                DateTime oldTime = tpsn[p.Id].HasValue ? tpsn[p.Id].Value.Time : tusr.trophyTimeInfoTable[p.Id].Time;
                try
                {
                    if (tpsn[p.Id].HasValue)
                        tpsn.DeleteTrophyByID(p.Id);
                    tusr.LockTrophy(p.Id);
                    tpsn.PutTrophy(p.Id, tusr.trophyTypeTable[p.Id].Type, p.Time);
                    tusr.UnlockTrophy(p.Id, p.Time);
                    applied++;
                }
                catch (Exception ex)
                {
                    failures.Add("#" + p.Id + " " + Shorten(tconf[p.Id].name, 30) + ": " + ex.Message);
                    // Put the trophy back the way it was.
                    try
                    {
                        if (tpsn[p.Id].HasValue)
                            tpsn.DeleteTrophyByID(p.Id);
                        tusr.LockTrophy(p.Id);
                        if (hadOld)
                        {
                            tpsn.PutTrophy(p.Id, tusr.trophyTypeTable[p.Id].Type, oldTime);
                            tusr.UnlockTrophy(p.Id, oldTime);
                        }
                    }
                    catch (Exception restoreEx)
                    {
                        failures.Add("#" + p.Id + " could not be restored: " + restoreEx.Message + " - close without saving if this matters.");
                    }
                }
            }

            if (applied > 0)
                haveBeenEdited = true;
            RefreshComponents();

            string message = "Imported " + applied + " of " + plan.Count + " trophy times.";
            if (applied > 0)
                message += "\nUse File > Save to write them to the trophy files.";
            message += FormatIssues(failures);
            MessageBox.Show(message, ImportTitle, MessageBoxButtons.OK,
                failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        #endregion

        #region Parsing helpers

        private static Cell Get(List<Cell> row, int col)
        {
            if (col < 0 || col >= row.Count)
                return new Cell();
            return row[col];
        }

        private static bool TryGetInteger(Cell cell, out int value)
        {
            value = -1;
            if (cell.Number.HasValue)
            {
                double d = cell.Number.Value;
                if (Math.Abs(d - Math.Round(d)) > 1e-9 || d < int.MinValue || d > int.MaxValue) return false;
                value = (int)Math.Round(d);
                return true;
            }
            string s = cell.Text.Trim().TrimStart('#');
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string NormalizeHeader(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (s ?? string.Empty).ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        private static string NormalizeName(string s)
        {
            s = (s ?? string.Empty)
                .Replace('’', '\'').Replace('‘', '\'')
                .Replace('“', '"').Replace('”', '"')
                .Replace(' ', ' ');
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.ToLowerInvariant();
        }

        private static string Shorten(string s, int max)
        {
            s = s ?? string.Empty;
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        private static string FormatIssues(List<string> issues)
        {
            if (issues == null || issues.Count == 0)
                return string.Empty;
            const int maxShown = 12;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Skipped / notes (" + issues.Count + "):");
            foreach (string issue in issues.Take(maxShown))
                sb.AppendLine("  • " + issue);
            if (issues.Count > maxShown)
                sb.Append("  ...and " + (issues.Count - maxShown) + " more");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The Windows "Region" format (the app overrides the thread culture with its UI language).</summary>
        private static CultureInfo UserRegionCulture()
        {
            try
            {
                string name = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\International", "LocaleName", null) as string;
                if (!string.IsNullOrEmpty(name))
                    return CultureInfo.GetCultureInfo(name);
            }
            catch { }
            return CultureInfo.InstalledUICulture;
        }

        private static readonly string[] IsoFormats =
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd H:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd",
            "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy/M/d H:mm:ss", "yyyy/M/d  H:mm:ss", "yyyy/M/d H:mm", "yyyy/M/d",
            "yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd HH:mm", "yyyy.MM.dd"
        };

        /// <summary>
        /// Parses a time cell. Accepts Excel date/time cells, Unix timestamps (seconds or milliseconds),
        /// ISO text such as 2014-03-18 22:05:31, text in the Windows region format (e.g. 18/03/2014 22:05),
        /// and PSNProfiles-style text such as "18th Mar 2014 10:05:31 PM". An optional separate time cell
        /// is added to a date-only value.
        /// </summary>
        private static bool TryParseCellDate(Cell dateCell, Cell timeCell, out DateTime result, out string error)
        {
            result = DateTime.MinValue;
            error = null;

            DateTime datePart;
            if (!TryParseSingle(dateCell, out datePart))
            {
                error = "couldn't understand the time \"" + Shorten(dateCell.ToString(), 40) + "\".";
                return false;
            }

            if (timeCell != null && !timeCell.IsEmpty)
            {
                TimeSpan tod;
                if (timeCell.Number.HasValue && timeCell.Number.Value >= 0 && timeCell.Number.Value < 1)
                    tod = TimeSpan.FromSeconds(Math.Round(timeCell.Number.Value * 86400));
                else
                {
                    DateTime t;
                    if (!DateTime.TryParse(timeCell.Text.Trim(), UserRegionCulture(), DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out t) &&
                        !DateTime.TryParse(timeCell.Text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out t))
                    {
                        error = "couldn't understand the time of day \"" + Shorten(timeCell.ToString(), 20) + "\".";
                        return false;
                    }
                    tod = t.TimeOfDay;
                }
                datePart = datePart.Date + tod;
            }

            result = datePart;
            return true;
        }

        private static bool TryParseSingle(Cell cell, out DateTime result)
        {
            result = DateTime.MinValue;

            double number;
            bool isNumber = cell.Number.HasValue;
            number = isNumber ? cell.Number.Value : 0;
            if (!isNumber)
            {
                string t = cell.Text.Trim();
                // A plain number typed as text (e.g. a Unix timestamp in a CSV).
                if (Regex.IsMatch(t, @"^\d+(\.\d+)?$") &&
                    double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                    isNumber = true;
            }

            if (isNumber)
            {
                try
                {
                    if (number >= 1e12 && number < 1e14)            // Unix milliseconds
                    {
                        result = ((long)(number / 1000)).TimeStampToDateTime();
                        return true;
                    }
                    if (number >= 1e8 && number < 1e11)             // Unix seconds
                    {
                        result = ((long)number).TimeStampToDateTime();
                        return true;
                    }
                    if (number >= 1 && number < 2958466)            // Excel serial date
                    {
                        result = DateTime.FromOADate(number);
                        // Round to whole seconds (Excel stores fractions of a day).
                        result = new DateTime((result.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
                        return true;
                    }
                }
                catch (ArgumentException) { }
                return false;
            }

            string text = cell.Text.Trim();
            if (text.Length == 0)
                return false;

            // "18th March 2014" -> "18 March 2014"
            text = Regex.Replace(text, @"(\d+)(st|nd|rd|th)\b", "$1", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s+(UTC|GMT)$", string.Empty, RegexOptions.IgnoreCase);

            DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces;
            if (DateTime.TryParseExact(text, IsoFormats, CultureInfo.InvariantCulture, styles, out result))
                return true;
            if (DateTime.TryParse(text, UserRegionCulture(), styles, out result))
                return true;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, styles, out result))
                return true;
            return false;
        }

        #endregion
    }
}
