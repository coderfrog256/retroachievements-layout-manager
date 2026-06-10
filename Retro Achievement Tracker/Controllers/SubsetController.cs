using Retro_Achievement_Tracker.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Retro_Achievement_Tracker.Controllers
{
    public sealed class SubsetController
    {
        private static readonly SubsetController instance = new SubsetController();
        internal RetroAchievementAPIClient RetroAchievementsAPIClient { get; set; }

        private readonly Dictionary<long, HashSet<long>> _knownGameAssociations = new Dictionary<long, HashSet<long>>(); // From v2 API, authoritative parent->child mapping.
        private readonly Dictionary<long, HashSet<long>> _suspectedGameAssociations = new Dictionary<long, HashSet<long>>(); // From v1 API, parent->child hints. May not include all subsets, children may not belong to parent.
        //TODO(FROG): Persist this!
        private readonly Dictionary<long, bool> _excludedSubsets = new Dictionary<long, bool>();

        public GameInfo GetSelectedSet(GameInfo gameInfoAndProgress)
        {
            // If the game is excluded and only one subset is included, return that subset.
            // Otherwise return the game.
            if(IsGameExcluded(gameInfoAndProgress))
            {
                var included = gameInfoAndProgress.Children
                    .Where(c=>!IsGameExcluded(c))
                    .ToList();
                if(included.Count == 1)
                {
                    return included[0];
                }
            }
            return gameInfoAndProgress;
        }

        // The v1 API has no indicator for if a game has subsets.
        // To work around this, we look at the game history, and associate the root game to possible subsets.
        // This association is done console+title-prefix matching.
        public bool HandleUntrackedSubsets(List<GameInfo> previouslyPlayed)
        {
            var newSubsetTracked = false;
            foreach (var game in previouslyPlayed)
            {
                var suffixStart = game.Title.LastIndexOf("[Subset");
                if (suffixStart > 0)
                {
                    var rootName = game.Title.Substring(0, suffixStart);
                    var parent = previouslyPlayed.Find(m => m.Title.Trim() == rootName.Trim() && m.ConsoleId == game.ConsoleId);
                    if (parent != null)
                    {
                        if (!_suspectedGameAssociations.TryGetValue(parent.Id, out var children))
                        {
                            children = new HashSet<long>();
                            _suspectedGameAssociations[parent.Id] = children;
                        }
                        newSubsetTracked |= children.Add(game.Id);
                    }
                }
            }
            return newSubsetTracked;
        }

        public async Task HandleSubsetProgress(GameInfo rootGame, List<GameInfo> previouslyPlayed)
        {
            await TrackKnownSubsets(rootGame);
            HandleUntrackedSubsets(previouslyPlayed);
            if (IsGameExcluded(rootGame))
            {
                // Special case. User has excluded the base set, wipe the achivement list, we'll just add the subsets.
                rootGame.Achievements.Clear();
            }

            // Inject the subsets into the root game's achievment list (if not excluded).
            // First try the known list, then guess if no known list available.
            if (_knownGameAssociations.TryGetValue(rootGame.Id, out var children))
            {
                foreach (var childId in children)
                {
                    await InjectChildAchievements(rootGame, childId);
                }
            }
            else if (_suspectedGameAssociations.TryGetValue(rootGame.Id, out children))
            {
                foreach (var childId in children)
                {
                    await InjectChildAchievements(rootGame, childId);
                }
            }
        }

        // If a game is loaded via the Extended or Session API, we can cache its parent id
        // so it'll show up even if the user hasn't recently earned a subset achievement.
        public void TrackSuspectedSubset(GameInfo gameInfo)
        {
            if (gameInfo.Parent != null)
            {
                if (!_suspectedGameAssociations.TryGetValue((long)gameInfo.Parent, out var children))
                {
                    children = new HashSet<long>();
                    _suspectedGameAssociations[(long)gameInfo.Parent] = children;
                }
                children.Add(gameInfo.Id);
            }
        }

        public void PopulateSubsetTable(GameInfo rootGame, TableLayoutPanel subsetLayoutTable, Func<Task> onChecked)
        {
            subsetLayoutTable.SuspendLayout();
            while (subsetLayoutTable.Controls.Count > 0)
            {
                Control c = subsetLayoutTable.Controls[0];
                subsetLayoutTable.Controls.RemoveAt(0);
                c.Dispose();
            }
            subsetLayoutTable.RowStyles.Clear();
            subsetLayoutTable.RowCount = 0;

            AddGameToTable(rootGame, subsetLayoutTable, onChecked);
            foreach (var child in rootGame.Children)
            {
                AddGameToTable(child, subsetLayoutTable, onChecked);
            }
            EnsureAtLeastOneCheckboxChecked(subsetLayoutTable);
            subsetLayoutTable.ResumeLayout(true);
        }

        private void AddGameToTable(GameInfo gameInfo, TableLayoutPanel subsetLayoutTable, Func<Task> onChecked)
        {
            int rowIndex = subsetLayoutTable.RowCount++;
            subsetLayoutTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 138F));

            PictureBox newPicBox = new PictureBox();
            newPicBox.Size = new Size(128, 128);
            newPicBox.SizeMode = PictureBoxSizeMode.Zoom;
            newPicBox.ImageLocation = gameInfo.BadgeUri;
            newPicBox.Anchor = AnchorStyles.Left;
            newPicBox.Margin = new Padding(5);

            Label newLabel = new Label();
            newLabel.Text = gameInfo.Title;
            newLabel.ForeColor = Color.White;
            newLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            newLabel.Dock = DockStyle.Fill;
            newLabel.TextAlign = ContentAlignment.MiddleLeft;
            newLabel.AutoEllipsis = true;
            newLabel.Margin = new Padding(5);

            CheckBox newCheck = new CheckBox();
            newCheck.Text = "Use Achievements";
            newCheck.ForeColor = Color.White;
            newCheck.Size = new Size(150, 30);
            newCheck.Anchor = AnchorStyles.Right;
            newCheck.Margin = new Padding(5);
            newCheck.Checked = !IsGameExcluded(gameInfo);
            newCheck.CheckedChanged += async (s, ev) => {
                // Flip inclusion
                _excludedSubsets[gameInfo.Id] = !IsGameExcluded(gameInfo);

                try
                {
                    // Re-render. Lock the checkboxes to avoid parallel reloads if user is rapidly (un)checking.
                    subsetLayoutTable.Controls.OfType<CheckBox>().ToList().ForEach(c => c.Enabled = false);
                    await onChecked();
                }
                finally
                {
                    // Re-enable UI.
                    EnsureAtLeastOneCheckboxChecked(subsetLayoutTable);
                }
            };

            subsetLayoutTable.Controls.Add(newPicBox, 0, rowIndex);
            subsetLayoutTable.Controls.Add(newLabel, 1, rowIndex);
            subsetLayoutTable.Controls.Add(newCheck, 2, rowIndex);
        }

        private void EnsureAtLeastOneCheckboxChecked(TableLayoutPanel subsetLayoutTable)
        {
            // Sanity check. Things behave weird if EVERYTHING is excluded.
            // Do not allow that, you must always have at least one set enabled.
            var enabledChecks = new List<CheckBox>();
            foreach(var checkbox in subsetLayoutTable.Controls.OfType<CheckBox>())
            {
                if (checkbox.Checked)
                {
                    enabledChecks.Add(checkbox);
                }
                else
                {
                    checkbox.Enabled = true;
                }
            }
            if (enabledChecks.Count == 1)
            {
                enabledChecks[0].Enabled = false;
            }
            else
            {
                enabledChecks.ForEach(c => c.Enabled = true);
            }
        }

        // Try to invoke the v2 API to get a known list of subsets for this game.
        // This API is in-development, so this is an optional step.
        private async Task TrackKnownSubsets(GameInfo rootGame)
        {
            // Cache known associations for the session. Worst case the user can just bounce the app if a new subset's added.
            if (_knownGameAssociations.ContainsKey(rootGame.Id))
            {
                return;
            }
            try
            {
                var gameInfoV2 = await RetroAchievementsAPIClient.GetV2GameInfo(rootGame.Id);
                _knownGameAssociations.Add(rootGame.Id, gameInfoV2.SubsetIds);
            }
            catch
            {
                // Intentionally empty, v2 lookups are not mandatory and the API may have changed in an incompatible way.
            }
        }

        // In order to get the achivement progress for subsets, we need to look up each child subset's psuedo-game, and push it into the root game.
        private async Task InjectChildAchievements(GameInfo rootGame, long childId)
        {
            var childProgress = await RetroAchievementsAPIClient.GetGameInfoAndProgress(childId);
            if (childProgress != null)
            {
                // Sanity check, make sure that this is actually a subset of the game.
                if (childProgress.Parent == null || childProgress.Parent != rootGame.Id)
                {
                    //Oops. Unnecessary network call.
                    return;
                }
                rootGame.Children.Add(childProgress);

                if (IsGameExcluded(childProgress))
                {
                    // User has opted out of this subset's achievements, skip them.
                    return;
                }

                foreach (var cheevo in childProgress.Achievements)
                {
                    // Avoid dupes, just in case RA someday bundles subsets with the root.
                    if (rootGame.Achievements.Find(c => c.Id == cheevo.Id) == null)
                    {
                        rootGame.Achievements.Add(cheevo);
                    }
                }
            }
        }

        // Silly that C# doesn't have "getOrDefault", you have to use a ternary.
        private bool IsGameExcluded(GameInfo game)
        {
            // If there is an explicit exclusion recorded, use it.
            // Otherwise, default to included for root games, excluded for subsets.
            return _excludedSubsets.TryGetValue(game.Id, out var excluded) ? excluded : game.Parent != null;
        }

        public static SubsetController Instance
        {
            get
            {
                return instance;
            }
        }
    }
}
