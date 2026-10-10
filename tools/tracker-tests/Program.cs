using GameTracker;

// Everything the overlay code would write (learned names, logs) goes to a throwaway folder, never to the real data\.
string scratch = Path.Combine(Path.GetTempPath(), "gametracker-tests");
Environment.SetEnvironmentVariable("GAMETRACKER_DATA", scratch);

// Checks for overlay\Modules\ against the real FF7R guide and game.json (copied next to this program). Each check
// prints only when it fails; the exit code is the number of failures.
int failed = 0, passed = 0;
void Check(string what, bool ok)
{
    if (ok) passed++;
    else { failed++; Console.WriteLine("FAIL  " + what); }
}

var ff7r = GameRegistry.Find("ff7r");
Check("game.json ff7r is found", ff7r is not null);
if (ff7r is null) return failed;
var types = ff7r.StepTypes ?? new Dictionary<string, StepType>();
var guide = Guide.Load(ff7r.GuideFile);
var normal = guide.ForMode(hard: false);
var steps = guide.Chapters.SelectMany(c => c.Objectives).ToList();
Objective Step(string id) => steps.Single(o => o.Id == id);
Chapter ChapterOf(string id, Guide g) => g.Chapters.Single(c => c.Objectives.Any(o => o.Id == id));
var rules = new GuideRules(types, new ItemMap());

// ---- The guide's data agrees with itself -------------------------------------------------------------------------
Check("step ids are unique", steps.Select(o => o.Id).Distinct().Count() == steps.Count);
foreach (var o in steps)
{
    Check($"{o.Id}: type '{o.Type}' is in game.json stepTypes", types.ContainsKey(o.Type));
    foreach (var (column, id) in new[] { ("after", o.After), ("revisit", o.Revisit), ("rewardOf", o.RewardOf) }.Concat((o.Needs ?? []).Select(n => ("needs", (string?)n))))
        if (id is not null) Check($"{o.Id}: {column} '{id}' is a step", steps.Any(s => s.Id == id));
    if (o.RewardOf is { } quest)
        Check($"{o.Id}: rewardOf is a quest or event of the same chapter", ChapterOf(o.Id, guide).Objectives.Any(q => q.Id == quest && rules.IsQuestOrEvent(q)));
    if (o.Warning is not null && o.WarningEn is null) Check($"{o.Id}: a warning has warningEn", false);
    if ((o.Closes is null) != (o.ClosesEn is null)) Check($"{o.Id}: closes and closesEn come together", false);
    if (o.Auto is { } auto) Check($"{o.Id}: auto '{auto}' is chapter, boss or yes", auto is "chapter" or "boss" or "yes");
}
foreach (var (name, type) in types)
    Check($"stepTypes {name}: role is known", type.Role is "story" or "quest" or "event" or "item" or "trophy");

// ---- Step roles ---------------------------------------------------------------------------------------------------
Check("story step", rules.IsStory(Step("c8-16-requests")));
Check("trophy", rules.IsTrophy(Step("c1-12-trophy")));
Check("discovery is an event", rules.IsEvent(Step("c6-03-collapsed")) && rules.IsQuestOrEvent(Step("c6-03-collapsed")));
Check("side quest", rules.IsQuest(Step("c8-19-kids-patrol")));
Check("materia is an item", rules.IsItem(steps.First(o => o.Type == "materia")));
Check("weapons are never lost", rules.TypeOf(steps.First(o => o.Type == "senjata")).NeverLost);
Check("music discs are unique", rules.TypeOf(steps.First(o => o.Type == "music disc")).Unique);
Check("accessories can be lost (sold)", !rules.TypeOf(steps.First(o => o.Type == "aksesori")).NeverLost);
Check("an unknown type has no role", rules.TypeOf(Step("c8-16-requests") with { Type = "nope" }).Role is null);

// ---- Rewards ------------------------------------------------------------------------------------------------------
Check("Nail Bat is the reward of Kids on Patrol (Normal)", GuideRules.RewardOf(Step("c8-20-nail-bat"), ChapterOf("c8-20-nail-bat", normal))?.Id == "c8-19-kids-patrol");
Check("Chocobo & Moogle is the reward of the vent fan", GuideRules.RewardOf(Step("c6-06-chocobo-moogle"), ChapterOf("c6-06-chocobo-moogle", guide))?.Id == "c6-05-vent-fan");
Check("a plain step is nobody's reward", GuideRules.RewardOf(Step("c8-16-requests"), ChapterOf("c8-16-requests", guide)) is null);
Check("REWARD", rules.RewardTag(Step("c3-10-iron-blade")) == "REWARD");
Check("REWARD BOSS", rules.RewardTag(Step("c5-11-metal-knuckles")) == "REWARD BOSS");
Check("REWARD CHAPTER", rules.RewardTag(Step("c7-12-titanium")) == "REWARD CHAPTER");
Check("no tag for an optional step", rules.RewardTag(Step("c3-10-iron-blade") with { Optional = true }) is null);
Check("no tag for a trophy", rules.RewardTag(Step("c1-12-trophy")) is null);
Check("chapter trophy", GuideRules.ChapterEndTrophy(Step("c1-12-trophy")));

// ---- Names ----------------------------------------------------------------------------------------------------------
Check("a discovery matches the game's title", GuideRules.SameQuest(Step("c6-03-collapsed"), "Collapsed Passageway"));
Check("a side quest matches its own name", GuideRules.SameQuest(Step("c8-19-kids-patrol"), "Kids on Patrol"));
Check("one story step, two quest names", GuideRules.NamedAs(Step("c8-16-requests") with { Name = "A / Requests for the Mercenary" }, "Requests for the Mercenary"));
var shiva = Step("c8-16-requests") with { Name = "Shiva" };
Check("'Shiva Materia' is the step 'Shiva'", rules.Matches(shiva, "Shiva Materia") && rules.SameItem(shiva, "Shiva Materia"));
Check("'Turbo Ether' is not the step 'Ether'", !rules.SameItem(shiva with { Name = "Ether" }, "Turbo Ether"));
Check("but a step 'Turbo Ether' contains 'Ether' (Matches)", rules.Matches(shiva with { Name = "Turbo Ether" }, "Ether"));
Check("a step does not match an unrelated item", !rules.Matches(shiva, "Ether"));
Check("tests write to the scratch folder", DataPaths.Data.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase));

// ---- Areas ----------------------------------------------------------------------------------------------------------
Check("area and floor from 'where'", GuideRules.AreaOf(shiva with { Where = "Connecting Passageway (B5): a chest" }) == ("Connecting Passageway", "B5"));
Check("area without floor", GuideRules.AreaOf(shiva with { Where = "Center District: shop" }) == ("Center District", null));
Check("no area in plain text", GuideRules.AreaOf(shiva with { Where = "Talk to Jessie, who waits" }) is null);

// ---- The tracker: ticks from the game, with a fake reader -----------------------------------------------------------
ProgressTrackerTests.Run(Check, types);

Console.WriteLine($"{passed} passed, {failed} failed");
return failed;
