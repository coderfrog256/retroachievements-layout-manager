using Retro_Achievement_Tracker.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Retro_Achievement_Tracker.Controllers
{
    public sealed class SubsetController
    {
        private static readonly SubsetController instance = new SubsetController();
        private static readonly Regex SubsetNameRegex = new Regex(@"\[Subset\s*-\s*(?<subset>[^\]]+)\]",RegexOptions.Compiled);
        internal RetroAchievementAPIClient RetroAchievementsAPIClient { get; set; }
        private readonly Dictionary<long, List<SubsetInfoV2>> _knownGameSubsets = new Dictionary<long, List<SubsetInfoV2>>(); // From v2 API, authoritative parent->child mapping.
        private readonly Dictionary<long, HashSet<long>> _suspectedGameAssociations = new Dictionary<long, HashSet<long>>(); // From v1 API, parent->child hints. May not include all subsets, children may not belong to parent.
        private readonly Dictionary<long, bool> _excludedSubsets = new Dictionary<long, bool>();

        public void RestoreFromCsv(string exclusionsCsv)
        {
            if(exclusionsCsv != null)
            {
                foreach (var exclusion in exclusionsCsv.Split(','))
                {
                    var gameAndIncluded = exclusion.Split('=');
                    if (gameAndIncluded.Length > 1 && long.TryParse(gameAndIncluded[0], out var gameId) && bool.TryParse(gameAndIncluded[1], out var decision))
                    {
                        _excludedSubsets[gameId] = decision;
                    }
                }
            }
        }

        public string SaveToCsv()
        {
            return string.Join(",", _excludedSubsets.Select(e => $"{e.Key}={e.Value}"));
        }

        // If the game is excluded and only one subset is included, return that subset.
        // Otherwise return the game.
        public GameInfo GetSelectedSet(GameInfo gameInfoAndProgress)
        {
            if(IsGameExcluded(gameInfoAndProgress))
            {
                var included = gameInfoAndProgress.Children
                    .Where(c=>!IsGameExcluded(c))
                    .ToList();
                if(included.Count == 1)
                {
                    // Cascade the user's unlocked achievements down to the child
                    // It may be anonymous (all locked) if it came from the V2 API.
                    return included[0].CopyWithUserAchievements(gameInfoAndProgress.Achievements);
                }
            }
            return gameInfoAndProgress;
        }

        // The V1 RA API has a "fun" behavior where getting an achievement from a subset will momentarily change your active game to the subset.
        // Maybe this is for history tracking?
        // Regardless, it breaks the flow as the core game with its subsets is replaced by only the subset for a few minutes before flipping back.
        // Avoid flapping the active game by checking if the 0th game is a subset of the current one. Return the current game first if this happens.
        public List<GameInfo> AvoidGameFlap(List<GameInfo> gameInfos, GameInfo currentGame)
        {
            if (gameInfos.Count > 0)
            {
                var latestGame = gameInfos[0];
                var subsetName = SubsetNameRegex.Match(latestGame.Title);
                if(subsetName.Success)
                {
                    // User has a subset as their most recent game. Check for if the currentGame being played has it as a child,
                    // to avoid flapping.
                    if (currentGame != null && currentGame.Children.Where(c => c.Title.Trim() == subsetName.Groups["subset"].Value).Any())
                    {
                        gameInfos.Insert(0, currentGame);
                    }
                    // If there is no first game, maybe they're launching after a subset is the newest.
                    // Try to find a match in the history
                    else if (currentGame == null)
                    {
                        var suffixStart = latestGame.Title.LastIndexOf("[Subset");
                        var rootName = latestGame.Title.Substring(0, suffixStart).Trim();
                        var rootIndex = gameInfos.FindIndex(g => g.Title.Trim() == rootName);
                        if(rootIndex > 0)
                        {
                            // Weird C# incantation to swap list elements by index
                            // Move the root game to the 0th position, swap the subset to where the root is.
                            (gameInfos[0], gameInfos[rootIndex]) = (gameInfos[rootIndex], gameInfos[0]);
                        }
                    }
                }
            }
            // Not a subset, or no reasonable fallback, just leave it as-is.
            return gameInfos;
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
            var oldRootAchievements = rootGame.Achievements.ToList();
            if (IsGameExcluded(rootGame))
            {
                // Special case. User has excluded the base set, wipe the achivement list, we'll just add the subsets.
                rootGame.Achievements.Clear();
            }

            try
            {
                // Inject the subsets into the root game's achievment list (if not excluded).
                // First try the known list, then guess if no known list available.
                if (_knownGameSubsets.TryGetValue(rootGame.Id, out var subsets))
                {
                    try
                    {
                        foreach (var subset in subsets)
                        {
                            await InjectChildAchievementsV2(rootGame, subset);
                        }
                        return;
                    }
                    catch
                    {

                    }
                }
                // V2 not available, or has failed. Fall back to V1.
                if (_suspectedGameAssociations.TryGetValue(rootGame.Id, out var children))
                {
                    foreach (var childId in children)
                    {
                        await InjectChildAchievementsV1(rootGame, childId);
                    }
                }
            }
            finally
            {
                // Edge case. If the user persisted an exclusion for the root game, but the subset tracked has been delisted
                // We need to readd the achievements and remove the exclusion
                // In normal operation this is handled by disabling the checkbox, but it won't work if it's delisted RA-side.
                if(rootGame.Achievements.Count == 0)
                {
                    rootGame.Achievements.AddRange(oldRootAchievements);
                    _excludedSubsets.Remove(rootGame.Id);
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
            if (_knownGameSubsets.ContainsKey(rootGame.Id))
            {
                return;
            }
            try
            {
                var subsets = await RetroAchievementsAPIClient.GetSubsetAchievementsV2(rootGame.Id);
                subsets.ForEach(s=>s.CopyFromParent(rootGame));
                _knownGameSubsets[rootGame.Id] = subsets;
            }
            catch
            {
                // Intentionally empty, v2 lookups are not mandatory and the API may have changed in an incompatible way.
            }
        }

        // In order to get the achivement progress for subsets, we need to look up each child subset's psuedo-game, and push it into the root game.
        private async Task InjectChildAchievementsV1(GameInfo rootGame, long childId)
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

        // Add the subset to the game's list of children. If enabled, check the user's progress in this subset
        // Lastly add the user's unlocked achievements, and the user-agnostic locked achievements to the root game.
        private async Task InjectChildAchievementsV2(GameInfo rootGame, SubsetInfoV2 subset)
        {
            rootGame.Children.Add(subset);
            if (IsGameExcluded(subset))
            {
                return;
            }
            foreach (var cheevo in await RetroAchievementsAPIClient.GetUserSubsetAchievementsV2(subset.Id))
            {
                // Avoid dupes, just in case RA someday bundles subsets with the root.
                if (rootGame.Achievements.Find(c => c.Id == cheevo.Id) == null)
                {
                    rootGame.Achievements.Add(cheevo);
                }
            }

            foreach (var cheevo in subset.Achievements)
            {
                // Only add the subset's user-agnostic achievement if we haven't already added an earned one above.
                if (rootGame.Achievements.Find(c => c.Id == cheevo.Id) == null)
                {
                    rootGame.Achievements.Add(cheevo);
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
