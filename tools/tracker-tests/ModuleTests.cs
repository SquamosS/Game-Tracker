namespace GameTracker;

/// <summary>The guide's other modules: distances, which steps are open, where you are and the way there.</summary>
static class ModuleTests
{
    public static void Run(Action<string, bool> check, IReadOnlyDictionary<string, StepType> types)
    {
        // ---- World: centimetres to metres, rounded as the overlay shows them ------------------------------------------
        check("world: 300/400 cm apart is 5 m", Math.Abs(World.Distance(new(0, 0, 0), new(300, 400, 0)) - 5) < 1e-9);
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
        area.Follow(new(9000, 0, 0)); // a jump (a load): no room, and no link learned from it
        area.Follow(new(500, 0, 0));
        check("area: a jump teaches no way", area.RouteTo(area.Here!, "Room C", null) is ["Room B", "Room C"]);
        area.Follow(new(1500, 0, 0));
        var inB = Step("c2-bat") with { Where = "Room B (B5): on the shelf" };
        check("area: a step in this room and on its floor is here", area.IsHere(inB));
        check("area: the same room on another floor is not", !area.IsHere(inB with { Where = "Room B (B2): upstairs" }));
        check("area: another room is not here", !area.IsHere(inB with { Where = "Room A: corner" }));
    }
}
