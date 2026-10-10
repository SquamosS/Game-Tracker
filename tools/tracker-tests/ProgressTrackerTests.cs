namespace GameTracker;

/// <summary>A game as the tests want it: chapter, inventory, flags, side quests, set by hand between polls.</summary>
sealed class FakeReader : GameReaderBase
{
    public int? Chapter = 1;
    public Dictionary<long, (int Id, int Count)> Slots = [];
    public HashSet<string> Flags = [];
    public HashSet<int>? LiveIds;
    public List<SideQuest> Quests = [];
    public FakeNames FakeNames = new();
    public GameObjective? Objective;
    public Func<GamePosition, GameLocation?>? Locate;
    public List<GameChest> ChestList = [];
    public Dictionary<string, bool> OpenFlags = [];

    public override string? Version => "test";
    public override int? ReadChapter() => Chapter;
    public override IGameNames Names => FakeNames;
    public override List<OwnedItem>? ReadOwned() => Slots.Select(s => new OwnedItem(s.Value.Id, s.Value.Count, 0, s.Key)).ToList();
    public override HashSet<int>? ReadLiveOwnedIds(IReadOnlyCollection<long> changedSlots) => LiveIds;
    public override HashSet<string>? ReadFlags() => [.. Flags];
    public override IReadOnlyList<SideQuest> SideQuests => Quests;
    public override GameLocation? ReadLocation(GamePosition p) => Locate?.Invoke(p);
    public override IReadOnlyList<GameChest> Chests => ChestList;
    public override bool? ChestOpened(GameChest chest) => OpenFlags.TryGetValue(chest.Id, out var open) ? open : null;
    public override GameObjective? ReadObjective(int chapter) => Objective;
    public override GameObjective? NewestObjective() => Objective;
    public override IReadOnlyList<GameObjective> Candidates => Objective is null ? [] : [Objective];

    /// <summary>The game's live objective named like a guide story step.</summary>
    public static GameObjective Live(int row, string title) => new(row, row, "$str_" + row, "$str_" + row + "_d", title, null, false);
}

/// <summary>Names the way FF7R's work: gil 20, consumables below 100, materia from 10000, "X Materia" also "X".</summary>
sealed class FakeNames : GameNamesBase
{
    public Dictionary<int, string> Items = new() { [20] = "Gil", [1001] = "Iron Blade", [10001] = "Fire Materia", [10002] = "Ice Materia", [257] = "Tango of Tears" };
    public Dictionary<string, string> FlagNames = [];
    public override string? Name(int id) => Items.GetValueOrDefault(id);
    public override string? FlagName(string flag) => FlagNames.GetValueOrDefault(flag);
    public override void Learn(int id, string name) => Items[id] = name;
    public override void LearnFlag(string flag, string name) => FlagNames[flag] = name;
    public override bool IsCurrency(int id) => id == 20;
    public override bool IsConsumable(int id) => id < 100;
    public override bool FitsStep(int id, string stepType) => (id >= 10000) == (stepType == "materia");
    public override string? ShortName(string itemName) => itemName.EndsWith(" Materia") ? itemName[..^8] : null;
}

sealed class FakeHost : IProgressHost
{
    public List<string> Notices = [];
    public List<HashSet<int>> HandedOver = [];
    public int Saves;
    public void Notify(string text) => Notices.Add(text);
    public void Save() => Saves++;
    public void Persist() => Saves++;
    public void Error(string text) => Notices.Add("error: " + text);
    public void ItemsHandedOver(HashSet<int> ids) => HandedOver.Add(ids);
    public void LogItems(IEnumerable<OwnedItem> items) { }
}

static class ProgressTrackerTests
{
    static Objective Step(string id, string type, string name, string? auto = null, string? rewardOf = null) =>
        new(id, type, name, "Somewhere: here", false, null, Auto: auto, RewardOf: rewardOf);

    /// <summary>A small guide in FF7R's step types: two chapters with story, items, a disc, rewards, a side quest.</summary>
    public static Guide SmallGuide() => new("Test", "draft", [],
    [
        new Chapter(1, "One", null,
        [
            Step("c1-start", "cerita", "Start"),
            Step("c1-sword", "senjata", "Iron Blade"),
            Step("c1-fire", "materia", "Fire"),
            Step("c1-disc", "music disc", "Tango of Tears"),
            Step("c1-boss", "cerita", "Boss"),
            Step("c1-ice", "materia", "Ice", auto: "chapter"),
            Step("c1-trophy", "trofi", "One Done", auto: "chapter"),
        ]),
        new Chapter(2, "Two", null,
        [
            Step("c2-next", "cerita", "Next"),
            Step("c2-kids", "side quest", "Kids on Patrol"),
            Step("c2-bat", "aksesori", "Nail Bat", rewardOf: "c2-kids"),
        ]),
    ]);

    public static void Run(Action<string, bool> check, IReadOnlyDictionary<string, StepType> types)
    {
        var reader = new FakeReader();
        var host = new FakeHost();
        var tracker = new ProgressTracker(reader, new GuideRules(types, reader.Names), DataPaths.Game("test"), DataPaths.GameLogs("test"), host)
        {
            Guide = SmallGuide(),
            Progress = new Progress { Chapter = 1 },
        };
        bool Done(string id) => tracker.Progress.Done.Contains(id);
        bool Poll() => tracker.Poll(out _);

        // First read: the disc owned is done wherever the guide lists it; nothing else is.
        reader.Slots = new() { [1] = (20, 500), [2] = (257, 1), [3] = (0, 0), [4] = (0, 0), [5] = (0, 0), [6] = (0, 0), [7] = (0, 0) };
        reader.LiveIds = [20, 257];
        Poll();
        check("tracker: in game after the first poll", tracker.InGame && tracker.DetectedChapter == 1);
        check("tracker: a unique item owned at the first read is ticked", Done("c1-disc"));
        check("tracker: nothing else is ticked at the first read", tracker.Progress.Done.Count == 1);
        check("tracker: the inventory counts as read", tracker.InventoryRead);

        // An item handed over in play ticks its step ("Fire Materia" is the step "Fire"), and its chest is told.
        reader.Slots[3] = (10001, 1);
        Poll();
        check("tracker: an item handed over ticks its step", Done("c1-fire"));
        check("tracker: the chest of an item handed over is told", host.HandedOver.Any(h => h.SetEquals([10001])));
        check("tracker: an auto-tick is announced", host.Notices.Any(n => n.Contains("Fire")));

        // Gil arriving ticks nothing, and an unknown item is kept to learn from your next tick.
        reader.Slots[1] = (20, 900);
        reader.Slots[4] = (9001, 1);
        int before = tracker.Progress.Done.Count;
        Poll();
        check("tracker: gil and an unknown item tick nothing", tracker.Progress.Done.Count == before);

        // Many slots changing at once is a save being loaded: no ticks from it, the ticks follow that save.
        reader.Slots[3] = (1001, 1); reader.Slots[5] = (10002, 1); reader.Slots[6] = (10003, 1); reader.Slots[7] = (10004, 1);
        reader.LiveIds = null; // the save's own copy is not told apart yet
        Poll();
        check("tracker: a loaded save is not items handed over", !Done("c1-sword") && !Done("c1-ice"));
        check("tracker: a loaded save waits to be matched", tracker.LoadPending);
        reader.LiveIds = [20, 1001, 257]; // the loaded save owns the sword and the disc, not the Fire Materia
        Poll();
        check("tracker: the loaded save's weapon is ticked", Done("c1-sword"));
        check("tracker: a materia the loaded save lacks stays as it was (can be sold or used)", Done("c1-fire"));
        reader.LiveIds = [20, 257];
        reader.Slots[3] = (10005, 1); reader.Slots[5] = (10006, 1); reader.Slots[6] = (10007, 1); reader.Slots[7] = (10008, 1);
        Poll(); // another load: the sword is gone, and a weapon is never sold, so that save has not got it
        check("tracker: a weapon the loaded save lacks is unticked", !Done("c1-sword"));

        // Playing on into chapter 2: chapter 1's story, its end reward and its trophy are done; the chapter is recapped.
        tracker.PendingRecap = null;
        reader.Chapter = 2;
        Poll();
        check("tracker: the next chapter is followed", tracker.Progress.Chapter == 2 && tracker.DetectedChapter == 2);
        check("tracker: the ended chapter's story steps are done", Done("c1-start") && Done("c1-boss"));
        check("tracker: the ended chapter's end reward is done", Done("c1-ice"));
        check("tracker: the ended chapter's trophy is done", Done("c1-trophy"));
        check("tracker: the ended chapter is recapped", tracker.PendingRecap?.Chapter == 1);
        check("tracker: the story step is the new chapter's", tracker.CurrentStory?.Id == "c2-next");

        // The game's objective shows: the loaded save's story position is settled, the tracker learns again.
        reader.Objective = FakeReader.Live(1, "Next");
        Poll();
        check("tracker: a load is settled once the game's objective shows", !tracker.LoadPending);
        check("tracker: the live objective is followed", tracker.LiveObjective?.Title == "Next");

        // A completion flag the names know ticks its step.
        reader.FakeNames.FlagNames["4C0:1"] = "c2-next";
        reader.Flags.Add("4C0:1");
        Poll();
        check("tracker: a known flag ticks its step", Done("c2-next"));

        // The game's quest page shows the side quest cleared.
        reader.Quests = [new SideQuest("Kids on Patrol", "080_SLU5B_q02", "99", true)];
        Poll();
        check("tracker: a side quest the game shows cleared is ticked", Done("c2-kids"));

        // You tick the Nail Bat right after an unknown item arrived: the item is learned as the Nail Bat.
        reader.Slots[5] = (9010, 1);
        Poll();
        tracker.Progress.Done.Add("c2-bat");
        tracker.LearnFrom("c2-bat");
        check("tracker: an unknown item is learned from your tick", reader.FakeNames.Name(9010) == "Nail Bat");
        check("tracker: an item from before a load is not learned", reader.FakeNames.Name(9001) is null);

        // Going back a chapter is another save being loaded: the later chapter's ticks are undone.
        reader.Chapter = 1;
        Poll();
        check("tracker: going back a chapter is a loaded save", tracker.LoadPending && tracker.Progress.Chapter == 1);
        check("tracker: the later chapter is not done in an older save", !Done("c2-next") && !Done("c2-kids") && !Done("c2-bat"));

        StoryGoesBack(check, types);
    }

    /// <summary>A save from earlier in the chapter: the story goes back to the game's objective, what follows is open again.</summary>
    static void StoryGoesBack(Action<string, bool> check, IReadOnlyDictionary<string, StepType> types)
    {
        var reader = new FakeReader { LiveIds = [20], Objective = FakeReader.Live(1, "Start") };
        reader.Slots = new() { [1] = (20, 500) };
        var tracker = new ProgressTracker(reader, new GuideRules(types, reader.Names), DataPaths.Game("test"), DataPaths.GameLogs("test"), new FakeHost())
        {
            Guide = SmallGuide(),
            Progress = new Progress { Chapter = 1, Done = ["c1-start", "c1-fire", "c1-boss", "c1-ice", "c1-trophy"] },
        };
        bool Done(string id) => tracker.Progress.Done.Contains(id);
        tracker.Poll(out _);
        check("story: the game's objective is the story step", tracker.CurrentStory?.Id == "c1-start" && !Done("c1-start"));
        check("story: later story steps are open again", !Done("c1-boss"));
        check("story: items after the next story step are open again", !Done("c1-ice"));
        check("story: items before it stay (they may have been picked up)", Done("c1-fire"));
        check("story: trophies stay (they belong to the account)", Done("c1-trophy"));
        check("story: the load is settled", !tracker.LoadPending);
    }
}
