using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace PS3TrophyIsGood
{
    public partial class MainAPP
    {
        private bool identityCopyMenuHookInstalled;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (identityCopyMenuHookInstalled)
                return;

            identityCopyMenuHookInstalled = true;
            toolStripMenuItem1.Click -= toolStripMenuItem1_Click;
            toolStripMenuItem1.Click += toolStripMenuItem1_Identity_Click;
        }

        private void toolStripMenuItem1_Identity_Click(object sender, EventArgs e)
        {
            if (tconf == null || tusr == null || tpsn == null)
                return;

            copyFrom.SetLocalTrophies(
                tconf.trophys.Select(trophy => new CopyFrom.LocalTrophyIdentity
                {
                    Id = trophy.id,
                    Name = trophy.name,
                    Detail = trophy.detail,
                    Type = trophy.ttype,
                    GroupId = trophy.gid
                })
            );

            if (copyFrom.ShowDialog(this) != DialogResult.OK)
                return;

            List<CopyFrom.Pair> copied = (copyFrom.checkBox1.Checked
                    ? copyFrom.smartCopyPairs()
                    : copyFrom.copyFromPairs())
                .OrderBy(pair => pair.Id)
                .ToList();

            if (copied.Count == 0)
            {
                MessageBox.Show(
                    "No trophies were matched. No trophies were modified.",
                    "Copy From",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (copied.Select(pair => pair.Id).Distinct().Count() != copied.Count ||
                copied.Any(pair => pair.Id < 0 || pair.Id >= tusr.trophyTimeInfoTable.Count))
            {
                MessageBox.Show(
                    "The copied trophy mapping contains invalid or duplicate local trophy IDs. No trophies were modified.",
                    "Copy From",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            Dictionary<int, DateTime> earnedTimes = new Dictionary<int, DateTime>();
            try
            {
                foreach (CopyFrom.Pair pair in copied)
                {
                    if (pair.Date != 0)
                        earnedTimes.Add(pair.Id, pair.Date.TimeStampToDateTime());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                // Copy only trophies that were matched 1:1. Source-only DLC and local-only trophies are untouched.
                foreach (CopyFrom.Pair pair in copied)
                {
                    if (tpsn[pair.Id].HasValue)
                        tpsn.DeleteTrophyByID(pair.Id);
                    tusr.LockTrophy(pair.Id);
                }

                foreach (CopyFrom.Pair pair in copied)
                {
                    if (!earnedTimes.TryGetValue(pair.Id, out DateTime time))
                        continue;

                    tusr.UnlockTrophy(pair.Id, time);
                    tpsn.PutTrophy(pair.Id, tusr.trophyTypeTable[pair.Id].Type, time);
                }

                haveBeenEdited = true;
                RefreshComponents();

                int unmatchedLocal = Math.Max(0, tusr.trophyTimeInfoTable.Count - copied.Count);
                int ignoredRemote = Math.Max(0, copyFrom.LastRemoteTrophyCount - copied.Count);
                string message = "Copied " + copied.Count + " matched trophies from " + copyFrom.LastSourceName + ".";
                if (unmatchedLocal > 0)
                    message += " " + unmatchedLocal + " local trophies were not present on the source page and were left unchanged.";
                if (ignoredRemote > 0)
                    message += " " + ignoredRemote + " source-only trophies were ignored.";

                MessageBox.Show(message, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                haveBeenEdited = true;
                RefreshComponents();
                MessageBox.Show(ex.Message, "Copy From", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
