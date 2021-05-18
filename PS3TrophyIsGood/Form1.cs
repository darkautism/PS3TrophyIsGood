using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TROPHYParser;

namespace PS3TrophyIsGood
{
    public partial class Form1 : Form
    {
        private const long MINIMUM_POSSIBLE_DATE = 633347424000000000;

        TROPCONF tconf;
        TROPTRNS tpsn;
        TROPUSR tusr;
        string path;
        string pathTemp;
        DateTimePickForm dtpForm = null;
        DateTimePickForm dtpfForInstant = null;
        CopyFrom copyFrom = null;
        bool haveBeenEdited = false;

        DateTime ps3Time = new DateTime(MINIMUM_POSSIBLE_DATE);
        DateTime lastSyncTrophyTime = new DateTime(MINIMUM_POSSIBLE_DATE);
        DateTime randomEndTime = DateTime.Now;

        bool isOpen = false;
        int baseGamaCount;

        private string txtDateTimeTmp;

        public Form1()
        {
            CultureInfo curinfo = null;
            switch (Properties.Settings.Default.Language)
            {
                case 0:
                    curinfo = new CultureInfo("zh-TW");
                    break;
                case 2:
                    curinfo = new CultureInfo("pt-BR");
                    break;
                case 1:
                default:
                    curinfo = CultureInfo.CreateSpecificCulture("en");
                    break;
            }

            Thread.CurrentThread.CurrentCulture = curinfo;
            Thread.CurrentThread.CurrentUICulture = curinfo;
            InitializeComponent();
            toolStripComboBox1.SelectedIndexChanged -= toolStripComboBox1_SelectedIndexChanged;
            toolStripComboBox1.SelectedIndex = Properties.Settings.Default.Language;
            toolStripComboBox1.SelectedIndexChanged += toolStripComboBox1_SelectedIndexChanged;
            Directory.CreateDirectory("profiles");
            var profiles = new DirectoryInfo("profiles").GetFiles("*.sfo").Select(p => p.Name).ToArray();
            toolStripComboBox2.Items.Add("Default Profile");
            toolStripComboBox2.Items.AddRange(profiles);
            toolStripComboBox2.SelectedIndex = 0;
            dateTimePicker1.CustomFormat = Properties.strings.DateFormatString;
            copyFrom = new CopyFrom();
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/darkautism/PS3TrophyIsGood");
            System.Diagnostics.Process.Start("https://www.youtube.com/user/TheDarkNachoXD");
        }

        private void 關閉ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseFile();
            Application.Exit();
        }

        private void 開啟ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                OpenFile(folderBrowserDialog1.SelectedPath);
            }
        }

        private bool ValidateSelectedDate(DateTime selectedDate)
        {
            if (DateTime.Compare(lastSyncTrophyTime, selectedDate) > 0)
            {
                MessageBox.Show(string.Format(Properties.strings.PsnSyncTime, lastSyncTrophyTime.ToString(Properties.strings.DateFormatString)));
                return false;
            }
            return true;
        }

        private void DeleteTrophy(int trophyId, ListViewItem lvi)
        {
            if (IsTrophySync(trophyId))
            {
                MessageBox.Show(Properties.strings.SyncedTrophyCanNotEdit);
            }
            else
            if (trophyId != 0 && tconf[trophyId].gid == 0 && IsTrophyGot(0))
            {
                MessageBox.Show(Properties.strings.CantLoclPlatinumBeforOther);
            }
            else
            if (MessageBox.Show(Properties.strings.DeleteTrophyConfirm, Properties.strings.Delete, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                tpsn.DeleteTrophyByID(trophyId);
                tusr.LockTrophy(trophyId);
                lvi.SubItems[4].Text = Properties.strings.no;
                lvi.BackColor = Color.LightGray;
                lvi.SubItems[6].Text = string.Empty;
                CompletionRates();
                haveBeenEdited = true;
            }
        }

        private bool UnlockTrophy(int trophyId, DateTime trophyTime, ListViewItem lvi)
        {
            if (trophyId == 0 && tconf.HasPlatinium && (GetCountBaseTrophiesGot() < baseGamaCount))
            {
                MessageBox.Show(Properties.strings.CantUnloclPlatinumBeforOther);
                return false;
            }
            else
            {
                if (ValidateSelectedDate(trophyTime))
                {
                    try
                    {
                        tpsn.PutTrophy(trophyId, tusr.trophyTypeTable[trophyId].Type, trophyTime);
                        tusr.UnlockTrophy(trophyId, trophyTime);
                        lvi.SubItems[4].Text = Properties.strings.yes;
                        lvi.BackColor = Color.White;
                        lvi.SubItems[6].Text = trophyTime.ToString(Properties.strings.DateFormatString);
                        CompletionRates();
                        haveBeenEdited = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
        }

        private bool ChangeTrophyTime(int trophyId, DateTime trophyTime, ListViewItem lvi)
        {
            if (IsTrophySync(trophyId))
            {
                MessageBox.Show(Properties.strings.SyncedTrophyCanNotEdit);
                return false;
            }
            else
            {
                if (ValidateSelectedDate(trophyTime))
                {
                    try
                    {
                        tpsn.ChangeTime(trophyId, trophyTime);
                        TROPUSR.TrophyTimeInfo tti = tusr.trophyTimeInfoTable[trophyId];
                        tti.Time = trophyTime;
                        tusr.trophyTimeInfoTable[trophyId] = tti;
                        lvi.SubItems[6].Text = trophyTime.ToString(Properties.strings.DateFormatString);
                        haveBeenEdited = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
        }

        private void RefreshComponents()
        {
            EmptyAllComponents();
            listViewEx1.BeginUpdate();
            for (int i = 0; i < tconf.Count; i++)
            {
                listViewEx1.LargeImageList.Images.Add("", Image.FromFile(path + @"\TROP" + string.Format("{0:000}", tconf[i].id) + ".PNG"));
                ListViewItem lvi = new ListViewItem();
                lvi.ImageIndex = i; // 在這裡imageid其實等於trophy ID   ex 白金0號, 1...
                lvi.Text = tconf[i].name;
                lvi.SubItems.Add(tconf[i].detail);
                lvi.SubItems.Add(tconf[i].ttype);
                lvi.SubItems.Add(tconf[i].hidden == "yes" ? Properties.strings.yes : Properties.strings.no);
                if (tpsn[i].HasValue)
                {
                    lvi.SubItems.Add(Properties.strings.yes);
                    lvi.SubItems.Add(tpsn[i].Value.IsSync ? Properties.strings.yes : Properties.strings.no);
                    lvi.SubItems.Add(tpsn[i].Value.Time.ToString(Properties.strings.DateFormatString));
                    lvi.BackColor = (tpsn[i].Value.IsSync ? Color.LightPink : lvi.BackColor = Color.White);
                }
                else
                {
                    lvi.SubItems.Add(tusr.trophyTimeInfoTable[i].IsGet ? Properties.strings.yes : Properties.strings.no);
                    lvi.SubItems.Add(tusr.trophyTimeInfoTable[i].IsSync ? Properties.strings.yes : Properties.strings.no);
                    
                    var tropTimeTxt = string.Empty;
                    if (tusr.trophyTimeInfoTable[i].Time.Ticks > 0)
                    {
                        tropTimeTxt = tusr.trophyTimeInfoTable[i].Time.ToString(Properties.strings.DateFormatString);
                    }
                    lvi.SubItems.Add(tropTimeTxt);

                    if (tusr.trophyTimeInfoTable[i].IsSync)
                        lvi.BackColor = Color.LightPink;
                    else if (tusr.trophyTimeInfoTable[i].IsGet)
                        lvi.BackColor = Color.White;
                    else
                        lvi.BackColor = Color.LightGray;
                }
                if (tconf[i].gid == 0)
                {
                    lvi.SubItems.Add("BaseGame");
                    baseGamaCount = i;
                }
                else lvi.SubItems.Add($"DLC{tconf[i].gid}");

                listViewEx1.Items.Add(lvi);
            }
            listViewEx1.EndUpdate();
            CompletionRates();
        }

        private void EmptyAllComponents()
        {
            listViewEx1.Items.Clear();
            listViewEx1.LargeImageList.Images.Clear();
            listViewEx1.LargeImageList.ImageSize = new Size(50, 50);
            this.Text = Application.ProductName;
            progressBar1.Maximum = 100;
            progressBar1.Value = 0;
            label2.Text = "00/00";
            label4.Text = "000/000";
        }

        private void CompletionRates()
        {
            int totalGrade = 0, getGrade = 0, isGetTrophyNumber = 0;
            for (int i = 0; i < tconf.Count; i++)
            {
                switch ((TropType)tusr.trophyTypeTable[i].Type)
                {
                    case TropType.Platinum:
                        totalGrade += (int)TropGrade.Platinum;
                        getGrade += IsTrophySync(i) ? (int)TropGrade.Platinum : 0;
                        break;
                    case TropType.Gold:
                        totalGrade += (int)TropGrade.Gold;
                        getGrade += IsTrophySync(i) ? (int)TropGrade.Gold : 0;
                        break;
                    case TropType.Silver:
                        totalGrade += (int)TropGrade.Silver;
                        getGrade += IsTrophySync(i) ? (int)TropGrade.Silver : 0;
                        break;
                    case TropType.Bronze:
                        totalGrade += (int)TropGrade.Bronze;
                        getGrade += IsTrophySync(i) ? (int)TropGrade.Bronze : 0;
                        break;
                }

                if (IsTrophySync(i)) isGetTrophyNumber++;
            }
            progressBar1.Maximum = totalGrade;
            progressBar1.Value = getGrade;
            label2.Text = isGetTrophyNumber + "/" + tconf.Count;
            label4.Text = getGrade + "/" + totalGrade;
            this.Text = Application.ProductName + "-[" + tconf.title_name + "]";
        }

        private bool IsTrophySync(int trophyID)
        {
            return (tpsn[trophyID].HasValue && tpsn[trophyID].Value.IsSync) || tusr.trophyTimeInfoTable[trophyID].IsSync;
        }

        private bool IsTrophyGot(int trophyID)
        {
            return tpsn[trophyID].HasValue || tusr.trophyTimeInfoTable[trophyID].IsGet;
        }

        private int GetCountBaseTrophiesGot()
        {
            int countBaseTrophiesGot = 0;
            for (int i = 0; i < tconf.trophys.Count; i++)
            {
                if (tconf[i].gid == 0 && IsTrophyGot(i))
                {
                    countBaseTrophiesGot++;
                }
            }
            return countBaseTrophiesGot;
        }

        private void listViewEx1_SubItemClicked(object sender, ListViewEx.SubItemEventArgs e)
        {
            int trophyID = e.Item.ImageIndex;// 在這裡imageid其實等於trophy ID   ex 白金0號, 1...
            if (e.SubItem == 6 && !IsTrophySync(trophyID))
            {
                DateTime trophyTime = DateTime.Now;
                if (IsTrophyGot(trophyID))
                {
                    if (tpsn[trophyID].HasValue)
                    {
                        trophyTime = tpsn[trophyID].Value.Time;
                    }
                    else
                    {
                        trophyTime = tusr.trophyTimeInfoTable[trophyID].Time;
                    }
                }
                txtDateTimeTmp = e.Item.SubItems[e.SubItem].Text;
                e.Item.SubItems[e.SubItem].Text = trophyTime.ToString(Properties.strings.DateFormatString);
                listViewEx1.StartEditing(dateTimePicker1, e.Item, e.SubItem);
            }
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);
                if (Directory.Exists(files[0]))
                {
                    e.Effect = DragDropEffects.All;
                }
            }
        }

        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);
            OpenFile(files[0]);
        }

        private void 重新整理ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RefreshComponents();
        }

        private void listViewEx1_SubItemEndEditing(object sender, ListViewEx.SubItemEndEditingEventArgs e)
        {
            if (isOpen)
            {
                DateTime selectedDate = Convert.ToDateTime(e.DisplayText);
                var trophyID = e.Item.ImageIndex;
                ListViewItem lvi = ((ListView)sender).SelectedItems[0];
                bool trophyChanged;
                if (tpsn[trophyID].HasValue)
                {
                    trophyChanged = ChangeTrophyTime(trophyID, selectedDate, lvi);
                }
                else
                {
                    trophyChanged = UnlockTrophy(trophyID, selectedDate, lvi);
                }

                if (!trophyChanged)
                {
                    e.DisplayText = txtDateTimeTmp;
                }
            }
        }

        private void listViewEx1_DoubleClick(object sender, EventArgs e)
        {
            int trophyID = ((ListView)sender).SelectedItems[0].ImageIndex;// 在這裡imageid其實等於trophy ID   ex 白金0號, 1...
            ListViewItem lvi = ((ListView)sender).SelectedItems[0];
            if (IsTrophySync(trophyID))
            { // 尚未同步的才可編輯
                MessageBox.Show(Properties.strings.SyncedTrophyCanNotEdit);
            }
            else if (tpsn[trophyID].HasValue)
            {
                DeleteTrophy(trophyID, lvi);
            }
            else
            {  // nonget
                if (trophyID == 0 && tconf.HasPlatinium && (GetCountBaseTrophiesGot() < baseGamaCount))
                {
                    MessageBox.Show(Properties.strings.CantUnloclPlatinumBeforOther);
                }
                else if (dtpForm.ShowDialog(this) == DialogResult.OK)
                {
                    UnlockTrophy(trophyID, dtpForm.dateTimePicker1.Value, lvi);
                }
            }
        }

        private void 存檔ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFile();
        }

        private void 關閉檔案CToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseFile();
        }

        private DateTime LastTrophyTime()
        {
            if (DateTime.Compare(tpsn.LastTrophyTime, tusr.LastTrophyTime) > 0)
            {
                return tpsn.LastTrophyTime;
            }
            return tusr.LastTrophyTime;
        }

        private void OpenFile(string path_in)
        {
            try
            {
                if (isOpen)
                {
                    CloseFile();
                }
                path = path_in;
                pathTemp = Utility.CopyTrophyDirToTemp(path_in);
                Utility.decryptTrophy(pathTemp);
                tconf = new TROPCONF(pathTemp);
                tpsn = new TROPTRNS(pathTemp);
                tusr = new TROPUSR(pathTemp);

                lastSyncTrophyTime = tusr.LastSyncTime;
                if (DateTime.Compare(tpsn.LastSyncTime, tusr.LastSyncTime) > 0)
                    lastSyncTrophyTime = tpsn.LastSyncTime;

                ps3Time = lastSyncTrophyTime;
                dtpForm = new DateTimePickForm(ps3Time);
                dtpfForInstant = new DateTimePickForm(ps3Time);

                RefreshComponents();
                isOpen = true;
                重新整理ToolStripMenuItem.Enabled = true;
                進階ToolStripMenuItem.Enabled = true;
            }
            catch (FileNotFoundException ex)
            {
                tconf = null;
                tpsn = null;
                tusr = null;
                GC.Collect();
                MessageBox.Show(string.Format(Properties.strings.FileNotFoundMsg, Path.GetFileName(ex.FileName)));
            }
            catch (Exception ex)
            {
                tconf = null;
                tpsn = null;
                tusr = null;
                GC.Collect();
                Console.WriteLine(ex.StackTrace);
                MessageBox.Show(ex.Message);
            }
        }

        private void SaveFile()
        {
            if (isOpen)
            {
                if (listViewEx1.IsEditing)
                    listViewEx1.EndEditing(true);
                tpsn.Save();
                tusr.Save();
                haveBeenEdited = false;
                string encPathTemp = Utility.GetTemporaryDirectory();
                try
                {
                    Utility.CopyTrophyData(pathTemp, encPathTemp, false);
                    Utility.encryptTrophy(encPathTemp, toolStripComboBox2.Text);
                    Utility.CopyTrophyData(encPathTemp, path, true);
                }
                finally
                {
                    Utility.DeleteDirectory(encPathTemp);
                }
                RefreshComponents();
            }
        }

        public bool CloseFile()
        {
            if (listViewEx1.IsEditing)
                listViewEx1.EndEditing(true);
            if (haveBeenEdited)
            {
                DialogResult dr = MessageBox.Show(Properties.strings.CloseConfirm, Properties.strings.Close, MessageBoxButtons.YesNoCancel);
                if (dr == DialogResult.Yes)
                {
                    SaveFile();
                }
                else if (dr == DialogResult.No)
                {
                }
                else
                {
                    return false;
                }
            }

            tpsn = null;
            tusr = null;
            tconf = null;
            path = string.Empty;
            pathTemp = string.Empty;
            haveBeenEdited = false;
            重新整理ToolStripMenuItem.Enabled = false;
            進階ToolStripMenuItem.Enabled = false;
            isOpen = false;
            EmptyAllComponents();
            if (!string.IsNullOrEmpty(pathTemp))
            {
                Utility.DeleteDirectory(new DirectoryInfo(pathTemp).Parent.FullName);
            }
            return true;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isOpen)
            {
                e.Cancel = !CloseFile();
            }
        }

        private void 瞬間白金ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Random rand = new Random((int)DateTime.Now.Ticks);
            int i;

            //Base game
            for (i = 1; i < tusr.trophyTimeInfoTable.Count && tconf[i].gid == 0; i++)
            {
                if (!IsTrophyGot(i))
                {
                    tusr.UnlockTrophy(i, new DateTime(Utility.LongRandom(ps3Time.Ticks, randomEndTime.Ticks, rand)));
                    tpsn.PutTrophy(i, tusr.trophyTypeTable[i].Type, new DateTime(Utility.LongRandom(ps3Time.Ticks, randomEndTime.Ticks, rand)));
                }
            }
            //Platinium game
            if (!IsTrophyGot(0))
            {
                tusr.UnlockTrophy(0, LastTrophyTime().AddSeconds(1));
                tpsn.PutTrophy(0, tusr.trophyTypeTable[0].Type, LastTrophyTime().AddSeconds(1));
            }

            //DLC 
            for (; i < tusr.trophyTimeInfoTable.Count; i++)
            {
                if (!IsTrophyGot(i))
                {
                    tusr.UnlockTrophy(i, new DateTime(Utility.LongRandom(ps3Time.Ticks, randomEndTime.Ticks, rand)));
                    tpsn.PutTrophy(i, tusr.trophyTypeTable[i].Type, new DateTime(Utility.LongRandom(ps3Time.Ticks, randomEndTime.Ticks, rand)));
                }
            }
            haveBeenEdited = true;
            RefreshComponents();
        }

        private void 清除獎杯ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TROPTRNS.TrophyInfo? ti = tpsn.PopTrophy();
            while (ti.HasValue)
            {
                tusr.LockTrophy(ti.Value.TrophyID);
                ti = tpsn.PopTrophy();
            }
            haveBeenEdited = true;
            RefreshComponents();
        }

        private void toolStripComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.Language = toolStripComboBox1.SelectedIndex;
            Properties.Settings.Default.Save();
            MessageBox.Show(Properties.strings.RestartProgram);
        }

        private void setRandomStartTimeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            dtpfForInstant.Title.Text = Properties.strings.RandomStartTime;
            if (dtpfForInstant.ShowDialog() == DialogResult.OK)
            {
                ps3Time = dtpfForInstant.dateTimePicker1.Value;
            }
        }

        private void setRandomEndTimeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            dtpfForInstant.Title.Text = Properties.strings.RandomEndTime;
            if (dtpfForInstant.ShowDialog() == DialogResult.OK)
            {
                randomEndTime = dtpfForInstant.dateTimePicker1.Value;
            }
        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (copyFrom.ShowDialog(this) == DialogResult.OK)
            {
                var _times = copyFrom.checkBox1.Checked ? copyFrom.smartCopy().ToList() : copyFrom.copyFrom().ToList();
                if (_times.Any()) 清除獎杯ToolStripMenuItem_Click(sender, e); // no idea why but sometimes it get bug and it don't update, so lockin first fix it
                try
                {
                    for (int i = 0; i < tusr.trophyTimeInfoTable.Count; ++i)
                    {

                        if (!tpsn[i].HasValue && _times[i] != 0)
                        {
                            var time = _times[i].TimeStampToDateTime();
                            tusr.UnlockTrophy(i, time);
                            tpsn.PutTrophy(i, tusr.trophyTypeTable[i].Type, time);
                        }
                    }
                    haveBeenEdited = true;
                    RefreshComponents();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

        }

        private void menuStrip1_Click(object sender, EventArgs e)
        {
            if (listViewEx1.IsEditing)
                listViewEx1.EndEditing(true);
        }
    }
}
