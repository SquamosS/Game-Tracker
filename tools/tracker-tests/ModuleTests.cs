namespace GameTracker;

/// <summary>The guide's other modules: distances, which steps are open, where you are and the way there.</summary>
static class ModuleTests
{
    public static void Run(Action<string, bool> check, IReadOnlyDictionary<string, StepType> types)
    {
        // ---- World: centimetres to metres, rounded as the overlay shows them ------------------------------------------
        check("world: 300/400 cm apart is 5 m (Unreal units)", Math.Abs(World.Distance(new(0, 0, 0), new(300, 400, 0), 100) - 5) < 1e-9);
        check("world: 3/4 m apart is 5 m (a game in metres)", Math.Abs(World.Distance(new(0, 0, 0), new(3, 4, 0), 1) - 5) < 1e-9);
        check("world: a reader without units says metres", new NoGameReader().UnitsPerMetre == 1);
        check("world: FF7R's reader says centimetres", ((IGameReader)new Ff7rChapterReader()).UnitsPerMetre == 100);
        check("world: 1 m steps up close", World.Metres(3.4) == "3 m");
        check("world: 5 m steps to 100 m", World.Metres(47) == "45 m");
        check("world: 10 m steps beyond", World.Metres(123) == "120 m");

        // ---- StepStatus: open or not yet -----------------------------------------------------------------------------
        var reader = new FakeReader();
        var rules = new GuideRules(types, reader.Names);
        var tracker = new ProgressTracker(reader, rules, DataPaths.Game("test"), DataPaths.GameLogs("test"), new FakeHost())
        {
            Guide = ProgressTrackerTests.SmallGuide(),
            Progress = new Progress { Chapter = 1 },
        };
        var status = new StepStatus(tracker, rules, reader);
        var one = tracker.Guide!.Chapters[0];
        Objective Step(string id) => tracker.Guide.Chapters.SelectMany(c => c.Objectives).Single(o => o.Id == id);
        check("status: a step of the current story phase is open", !status.NotYet(Step("c1-fire"), one));
        check("status: a step after the next story step is not yet", status.NotYet(Step("c1-ice"), one));
        check("status: After decides wherever the step is listed", status.NotYet(Step("c1-fire") with { After = "c1-boss" }, one));
        tracker.Progress.Done.Add("c1-start");
        check("status: the current story step counts as reached", status.Reached("c1-boss"));
        check("status: once the story moves on, the next phase opens", !status.NotYet(Step("c1-ice"), one));
        tracker.Progress.Chapter = 2;
        var two = tracker.Guide.Chapters[1];
        check("status: no quest page entry, the guide order decides", status.SideQuestOpen(Step("c2-kids")) is null);
        reader.Quests = [new SideQuest("Someone Else", "q09", "00", false)];
        check("status: a quest page without this chapter's quests cannot tell", status.SideQuestOpen(Step("c2-kids")) is null);
        reader.Quests = [new SideQuest("Kids on Patrol", "q02", "00", false)];
        check("status: the quest page lists it: open", status.SideQuestOpen(Step("c2-kids")) == true && !status.NotYet(Step("c2-kids"), two));
        check("under way: a quest only offered is not under way", status.QuestsUnderWay().Count == 0);
        reader.Quests = [new SideQuest("Kids on Patrol", "q02", "10", false, true, "Find the kids.")];
        check("under way: a quest taken shows with its stage's text and guide step",
            status.QuestsUnderWay() is [("Kids on Patrol", "Find the kids.", { Id: "c2-kids" })]);
        reader.Quests = [.. reader.Quests, new SideQuest("Kids on Patrol", "q02", "20", false, true, "Go back.")];
        check("under way: two stages with different texts: the title without a text", status.QuestsUnderWay() is [("Kids on Patrol", null, _)]);
        reader.Quests = [new SideQuest("Kids on Patrol", "q02", "99", true, true, "Done.")];
        check("under way: a cleared quest is not", status.QuestsUnderWay().Count == 0);
        reader.Quests = [new SideQuest("Kids on Patrol", "q02", "00", false)];

        // ---- AreaTracker: where you are, rooms walked between, the way to a step ---------------------------------------
        // Three rooms in a row along X, 10 m wide; Room B has floors.
        reader.Locate = p => p.X switch
        {
            < 1000 => new GameLocation("Room A", null),
            < 2000 => new GameLocation("Room B", "B5"),
            < 3000 => new GameLocation("Room C", null),
            _ => null,
        };
        var area = new AreaTracker(reader, tracker, DataPaths.Game("test-area"), DataPaths.GameLogs("test-area"));
        check("area: unknown before the first position", area.Here is null);
        area.Follow(new(500, 0, 0));
        check("area: the room you stand in", area.Here?.Area == "Room A");
        area.Follow(new(1500, 0, 0));
        area.Follow(new(2500, 0, 0));
        check("area: walking on changes the room", area.Here?.Area == "Room C");
        check("area: the way back through the rooms walked", area.RouteTo(area.Here!, "Room A", null) is ["Room B", "Room A"]);
        check("area: no way to a room never walked to", area.RouteTo(area.Here!, "Room Z", null) is null);
        area.Follow(new(500, 0, 0)); // from Room C straight into Room A, 20 m in a second: a load or a cutscene, not a walk
        check("area: a jump teaches no way", area.RouteTo(area.Here!, "Room C", null) is ["Room B", "Room C"]);
        area.Follow(new(1500, 0, 0));
        var inB = Step("c2-bat") with { Where = "Room B (B5): on the shelf" };
        check("area: a step in this room and on its floor is here", area.IsHere(inB));
        check("area: the same room on another floor is not", !area.IsHere(inB with { Where = "Room B (B2): upstairs" }));
        check("area: another room is not here", !area.IsHere(inB with { Where = "Room A: corner" }));

        // ---- ChestGuide: the one chest holding a step's item, how far, and the chests left here ----------------------
        tracker.Progress.Chapter = 1;
        tracker.Progress.Done.Clear();
        var chest = new GameChest("obt010_treasure0010", new GamePosition(1500, 300, 0), [10001]);
        reader.ChestList = [chest];
        var chests = new ChestGuide(reader, tracker, rules, area, status, true, DataPaths.Game("test-chests"), DataPaths.GameLogs("test-chests"));
        chests.Load();
        var fire = Step("c1-fire") with { Where = "Room B: a chest by the door" };
        area.Follow(new(1200, 300, 0));
        check("chests: the distance to the one chest holding the step's item", chests.ChestDistance(fire) == "3 m");
        check("chests: no distance when the guide puts the step in another area", chests.ChestDistance(fire with { Where = "Room A: corner" }) is null);
        check("chests: the chests left in this area", chests.ChestsHere() is [("Fire Materia", "3 m")]);
        reader.OpenFlags[chest.Id] = true;
        check("chests: the game's flag says opened: no distance", chests.ChestDistance(fire) is null && chests.ChestsHere().Count == 0);
        reader.OpenFlags.Clear();
        area.Follow(new(1400, 300, 0));
        chests.LearnOpened([10001], new GameState(false, false, false, true, 0, ""));
        check("chests: an item arriving in a battle opens no chest", chests.ChestsHere().Count == 1);
        chests.LearnOpened([10001], null);
        check("chests: its item arriving within 4 m opens the chest", chests.ChestsHere().Count == 0 && chests.ChestDistance(fire) is null);

        // ---- TrailRecorder: the person giving a side quest, standing in the level now ---------------------------------
        var givers = typeof(Ff7rChapterReader).GetInterfaceMap(typeof(IGameReader));
        int giversAt = Array.FindIndex(givers.InterfaceMethods, m => m.Name == nameof(IGameReader.ReadQuestGivers));
        check("giver: FF7R's reader answers who gives a quest itself (not the interface's 'not known')", givers.TargetMethods[giversAt].DeclaringType == typeof(Ff7rChapterReader));
        check("giver: a reader without it does not know", new NoGameReader().ReadQuestGivers() is null);
        tracker.Progress.Chapter = 2;
        var trails = new TrailRecorder(reader, tracker, rules, area, status, true, DataPaths.Game("test-trail"), DataPaths.GameLogs("test-trail"));
        var kids = Step("c2-kids");
        area.Follow(new(1200, 300, 0));
        check("giver: none known, no distance", trails.PointDistance(kids) is null);
        reader.Givers = [new QuestGiver("Kids on Patrol", new GamePosition(1500, 700, 0))];
        check("giver: the distance to the person giving the quest", trails.PointDistance(kids) == "5 m");
        check("giver: not for another step (its reward)", trails.PointDistance(Step("c2-bat")) is null);
        check("giver: only for a side quest step, not an item named like it", trails.PointDistance(kids with { Id = "c2-kids-item", Type = "aksesori" }) is null);
        reader.Givers = [.. reader.Givers, new QuestGiver("Kids on Patrol", new GamePosition(0, 0, 0))];
        check("giver: two people for one quest is a guess: no distance", trails.PointDistance(kids) is null);
        reader.Givers = [new QuestGiver("Kids on Patrol", new GamePosition(1500, 700, 0))];
        tracker.Progress.Done.Add("c2-kids");
        check("giver: a quest done has no distance", trails.PointDistance(kids) is null);
        tracker.Progress.Done.Remove("c2-kids");
        reader.Givers = null;

        // ---- TrailRecorder: what the game's map marks for a side quest under way -------------------------------------
        var markers = typeof(Ff7rChapterReader).GetInterfaceMap(typeof(IGameReader));
        int markersAt = Array.FindIndex(markers.InterfaceMethods, m => m.Name == nameof(IGameReader.ReadQuestMarkers));
        check("marker: FF7R's reader answers what the map marks itself (not the interface's 'not known')", markers.TargetMethods[markersAt].DeclaringType == typeof(Ff7rChapterReader));
        check("marker: a reader without it does not know", new NoGameReader().ReadQuestMarkers() is null);
        reader.Markers = [new QuestMarker("Kids on Patrol", null, new GamePosition(1200, 2300, 0)),
            new QuestMarker("Kids on Patrol", "Child01", new GamePosition(1200, 1300, 0)), new QuestMarker("Kids on Patrol", "Child02", new GamePosition(1500, 700, 0))];
        check("marker: the nearest of the quest's targets", trails.PointDistance(kids) == "5 m");
        reader.Markers = [.. reader.Markers, new QuestMarker("Kids on Patrol", null, new GamePosition(1200, 400, 0))];
        check("marker: targets come before the quest's own marker, even a nearer one", trails.PointDistance(kids) == "5 m");
        reader.Markers = [new QuestMarker("Kids on Patrol", null, new GamePosition(1200, 2300, 0))];
        check("marker: no targets left: the quest's own marker", trails.PointDistance(kids) == "20 m");
        reader.Markers = [.. reader.Markers, new QuestMarker("Kids on Patrol", null, new GamePosition(0, 0, 0))];
        check("marker: two markers of the quest itself is a guess: no distance", trails.PointDistance(kids) is null);
        reader.Markers = [new QuestMarker("Kids on Patrol", "Child01", new GamePosition(1500, 700, 0))];
        check("marker: not for another step", trails.PointDistance(Step("c2-bat")) is null);
        check("marker: only for a side quest step", trails.PointDistance(kids with { Id = "c2-kids-item", Type = "aksesori" }) is null);
        tracker.Progress.Done.Add("c2-kids");
        check("marker: a quest done has no distance", trails.PointDistance(kids) is null);
        tracker.Progress.Done.Remove("c2-kids");
        reader.Givers = [new QuestGiver("Kids on Patrol", new GamePosition(1200, 400, 0))];
        check("marker: the giver of a quest not taken comes first", trails.PointDistance(kids) == "1 m");
        reader.Givers = null;
        reader.Markers = null;

        // ---- Radar: the spots around you -----------------------------------------------------------------------------
        var radarReader = new FakeReader { Chapter = 2, LiveIds = [20], Objective = FakeReader.Live(7, "Back Home") };
        radarReader.Slots = new() { [1] = (20, 1) };
        var radarRules = new GuideRules(types, radarReader.Names);
        var radarTracker = new ProgressTracker(radarReader, radarRules, DataPaths.Game("test-radar"), DataPaths.GameLogs("test-radar"), new FakeHost())
        {
            Guide = ProgressTrackerTests.SmallGuide(),
            Progress = new Progress { Chapter = 2 },
        };
        radarTracker.Poll(out _);
        var radarStatus = new StepStatus(radarTracker, radarRules, radarReader);
        var radarArea = new AreaTracker(radarReader, radarTracker, DataPaths.Game("test-radar"), DataPaths.GameLogs("test-radar"));
        var radar = new Radar(radarReader, radarTracker, radarStatus, new ChestGuide(radarReader, radarTracker, radarRules, radarArea, radarStatus, true, DataPaths.Game("test-radar"), DataPaths.GameLogs("test-radar")));
        check("radar: nothing known, nothing shown", radar.Spots().Count == 0);
        static QuestMarker Mark(string title, string? target, float x, float y) => new(title, target, new GamePosition(x, y, 0));
        radarReader.Quests = [new SideQuest("Kids on Patrol", "q02", "11", false, true, "Find them.")];
        radarReader.Markers = [Mark("Kids on Patrol", null, 0, 0), Mark("Kids on Patrol", "Child01", 100, 0), Mark("Kids on Patrol", "Child02", 200, 0),
            Mark("Back Home", null, 0, 500), Mark("Someone Else", "Child01", 9, 9)];
        radarReader.Givers = [new QuestGiver("Weapons on a Rampage", new GamePosition(0, -300, 0))];
        var spots = radar.Spots();
        check("radar: a side quest under way shows its targets, not its own marker", spots.Count(s => s.Kind == RadarKind.Quest) == 2 && !spots.Any(s => s.Kind == RadarKind.Quest && s.At.X == 0));
        check("radar: the givers of quests not taken", spots.Count(s => s.Kind == RadarKind.Giver) == 1);
        check("radar: the live story objective's marker", radarTracker.LiveObjective?.Title == "Back Home" && spots.Where(s => s.Kind == RadarKind.Story).Select(s => s.At.Y).SequenceEqual([500f]));
        check("radar: what is marked for a quest not under way does not show", !spots.Any(s => s.At.X == 9));
        radarReader.Quests = [new SideQuest("Kids on Patrol", "q02", "99", true, false, "Done.")];
        check("radar: a quest cleared shows nothing", !radar.Spots().Any(s => s.Kind == RadarKind.Quest));
        check("radar: east, north and up in metres", Radar.Offset(radarReader, new(100, 200, 0), new(400, 600, 300)) == new RadarOffset(3, 4, 3));
        var radarMap = typeof(Ff7rChapterReader).GetInterfaceMap(typeof(IGameReader));
        bool Own(string name) => radarMap.TargetMethods[Array.FindIndex(radarMap.InterfaceMethods, m => m.Name == name)].DeclaringType == typeof(Ff7rChapterReader);
        check("radar: FF7R's reader gives its heading and map axes itself", Own(nameof(IGameReader.ReadHeading)) && Own(nameof(IGameReader.OnMap)));
        check("radar: a reader without them: no heading, X east and Y north", new NoGameReader().ReadHeading() is null && ((IGameReader)new NoGameReader()).OnMap(new(1, 2, 3)) == (1, 2));

        // ---- Checklist: which steps show, with which tag -------------------------------------------------------------
        var list = new Checklist(tracker, rules, status, area);
        tracker.Guide = ProgressTrackerTests.SmallGuide();
        tracker.Progress = new Progress { Chapter = 1 };
        var ch1 = tracker.Guide.Chapters[0].Objectives;
        int Current() => tracker.CurrentStory is { } s ? Array.IndexOf(ch1, s) : ch1.Length;
        area.Follow(new(2500, 0, 0)); // Room C: no step here
        var open = list.OpenSteps(ch1, Current());
        check("checklist: the current phase's open items show", open.Select(x => x.Step.Id).SequenceEqual(["c1-sword", "c1-fire", "c1-disc"]));
        check("checklist: no story steps, trophies or later phases", !open.Any(x => x.Step.Id is "c1-start" or "c1-boss" or "c1-ice" or "c1-trophy"));
        tracker.Progress.Done.Add("c1-start");
        open = list.OpenSteps(ch1, Current());
        check("checklist: steps of a phase left behind are tagged BEHIND", open.Where(x => x.Step.Id == "c1-sword").Select(x => x.Tag).SingleOrDefault() == Checklist.TagBehind);
        check("checklist: the next phase opens", open.Any(x => x.Step.Id == "c1-ice"));
        check("checklist: full list: NOW for the current phase, BEHIND before", list.FullTag(ch1[5], false, 4, 4) == Checklist.TagNow && list.FullTag(ch1[1], false, 0, 4) == Checklist.TagBehind
            && list.FullTag(ch1[1], true, 0, 4) is null && list.FullTag(ch1[4], false, 4, 4) is null);

        // ---- Manual ticks ------------------------------------------------------------------------------------------
        var host = new FakeHost();
        var manual = new ProgressTracker(reader, rules, DataPaths.Game("test"), DataPaths.GameLogs("test"), host)
        {
            Guide = ProgressTrackerTests.SmallGuide(),
            Progress = new Progress { Chapter = 1, Done = ["c1-start"], History = ["c1-start"] },
        };
        var c1 = manual.Guide!.Chapters[0].Objectives;
        check("manual: the next step is the first open one after the last finished story step", manual.NextStep(c1)?.Id == "c1-sword");
        manual.SetDone("c1-sword", true);
        check("manual: a tick is saved and kept for undo", manual.Progress.Done.Contains("c1-sword") && manual.Progress.History[^1] == "c1-sword" && host.Saves == 1);
        int saves = host.Saves;
        check("manual: undo unticks the last tick", manual.Undo() == "c1-sword" && !manual.Progress.Done.Contains("c1-sword"));
        check("manual: undo leaves saving to the window (it tells what it undid first)", host.Saves == saves);
        check("manual: nothing to undo twice over the start", manual.Undo() == "c1-start" && manual.Undo() is null);
        saves = host.Saves;
        manual.JumpTo(c1[5]); // "I am at the Ice Materia": the story before it is done
        check("manual: jumping to a step is saved", host.Saves == saves + 1);
        check("manual: jumping to a step finishes the story before it", manual.Progress.Done.Contains("c1-start") && manual.Progress.Done.Contains("c1-boss") && !manual.Progress.Done.Contains("c1-ice"));
        check("manual: next chapter", manual.ChangeChapter(+1) && manual.Progress.Chapter == 2);
        check("manual: no chapter after the last", !manual.ChangeChapter(+1) && manual.Progress.Chapter == 2);

        // ---- Two stories (FF7R's INTERmission): a save of one does not touch the other's ticks ----------------------
        var twoStories = ProgressTrackerTests.SmallGuide();
        twoStories = twoStories with { Chapters = [.. twoStories.Chapters, new Chapter(21, "INTERmission 1", null, [new("c21-start", "cerita", "Wutai", "Somewhere: here", false, null)], Story: "INTERmission")] };
        var storyReader = new FakeReader { Chapter = 1, LiveIds = [20] };
        storyReader.Slots = new() { [1] = (20, 1) };
        var stories = new ProgressTracker(storyReader, new GuideRules(types, storyReader.Names), DataPaths.Game("test"), DataPaths.GameLogs("test"), new FakeHost())
        {
            Guide = twoStories,
            Progress = new Progress { Chapter = 21, Done = ["c21-start", "c1-start", "c2-next"], Ever = ["c21-start", "c1-start", "c2-next"] },
        };
        stories.Poll(out _); // a main-story save of chapter 1 is loaded after playing the INTERmission
        check("stories: a main-story save does not untick the INTERmission (chapter 21 is not its future)", stories.Progress.Done.Contains("c21-start"));
        check("stories: the main story's later chapter is unticked as usual", !stories.Progress.Done.Contains("c2-next") && stories.Progress.Chapter == 1);

        var interReader = new FakeReader { Chapter = 21, LiveIds = [20, 9001] };
        interReader.Slots = new() { [1] = (20, 1), [2] = (9001, 1) };
        interReader.FakeNames.Items[9001] = "Nail Bat";
        var inter = new ProgressTracker(interReader, new GuideRules(types, interReader.Names), DataPaths.Game("test"), DataPaths.GameLogs("test"), new FakeHost())
        {
            Guide = twoStories,
            Progress = new Progress { Chapter = 2, Done = ["c1-start"], Ever = ["c1-start"] },
        };
        inter.Poll(out _); // an INTERmission save holding a Nail Bat is loaded
        check("stories: an INTERmission save's inventory ticks nothing of the main story", !inter.Progress.Done.Contains("c2-bat"));
        check("stories: and leaves the main story's ticks", inter.Progress.Done.Contains("c1-start") && inter.Progress.Chapter == 21);

        // ---- The chapter recap: the missables never ticked, once the inventory had time to catch up -------------------
        var missable = ProgressTrackerTests.SmallGuide();
        tracker.Guide = missable with { Chapters = missable.Chapters.Select(c => c with { Objectives = c.Objectives.Select(o => o.Id == "c1-fire" ? o with { Missable = true } : o).ToArray() }).ToArray() };
        tracker.PendingRecap = (1, DateTime.Now.AddSeconds(-30));
        check("recap: not before 90 s", tracker.DueRecap() is null && tracker.PendingRecap is not null);
        tracker.PendingRecap = (1, DateTime.Now.AddSeconds(-100));
        check("recap: the missables never ticked", tracker.DueRecap() is (1, [{ Id: "c1-fire" }]) && tracker.PendingRecap is null);
        tracker.Progress.Done.Add("c1-fire");
        tracker.PendingRecap = (1, DateTime.Now.AddSeconds(-100));
        check("recap: nothing missed", tracker.DueRecap() is (1, []));
    }
}
