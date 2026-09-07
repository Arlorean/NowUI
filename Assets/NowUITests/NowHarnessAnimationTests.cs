using System.Collections.Generic;
using NUnit.Framework;
using NowUI.Editor;

public class NowHarnessAnimationTests
{
    [Test]
    public void FrameTimeIsDerivedOnlyFromFrameIndexAndRate()
    {
        var frame = new NowHarnessAnimationFrame(index: 17, count: 60, framesPerSecond: 30f);

        Assert.AreEqual(17, frame.index);
        Assert.AreEqual(60, frame.count);
        Assert.AreEqual(17f / 30f, frame.timeSeconds, 0.000001f);
        Assert.AreEqual(1f / 30f, frame.deltaTimeSeconds, 0.000001f);
        Assert.AreEqual(2f, frame.durationSeconds, 0.000001f);
        Assert.AreEqual(17f / 60f, frame.normalizedTime, 0.000001f);
    }

    [Test]
    public void LoopTimingDoesNotDuplicateTheFirstFrameAtTheEnd()
    {
        var finalFrame = new NowHarnessAnimationFrame(index: 59, count: 60, framesPerSecond: 30f);

        Assert.Less(finalFrame.normalizedTime, 1f);
        Assert.AreEqual(59f / 60f, finalFrame.normalizedTime, 0.000001f);
        Assert.AreEqual(59f / 30f, finalFrame.timeSeconds, 0.000001f);
    }

    [Test]
    public void ScenarioValidationRejectsUnsafeOutputNames()
    {
        var scenario = new NowHarnessAnimationScenario(
            "../outside",
            640,
            360,
            30,
            30f,
            (rect, frame) => { });

        Assert.Throws<System.InvalidOperationException>(() => scenario.Validate());
    }

    [Test]
    public void ReadmeShowcasesShareOneCaptureFormat()
    {
        // Every README loop is embedded at the same size and rate, so the media read as one set rather than as a
        // collection of differently shaped clips. That is the invariant worth defending here.
        //
        // This deliberately does NOT check the roster of scenarios or their individual frame counts. An earlier version
        // did, by hard-coding the six names and durations that existed at the time, and it failed the moment two more
        // loops were added -- which is a change the project wanted, not a regression. A test that restates the data it
        // is checking has no oracle of its own: it can only report that the data changed, which git already does.
        // Frame counts in particular are partly computed in the harness, so hard-coding them duplicated that logic too.
        //
        // The per-frame timing maths that used to be repeated here for each scenario is covered, independently of any
        // scenario, by FrameTimeIsDerivedOnlyFromFrameIndexAndRate and LoopTimingDoesNotDuplicateTheFirstFrameAtTheEnd.
        var scenarios = NowHarnessAnimationScenarios.All();

        Assert.Greater(scenarios.Count, 0, "the README showcase harness declares no scenarios");

        var names = new HashSet<string>();

        foreach (var scenario in scenarios)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(scenario.name), "a scenario has no name");
            Assert.IsTrue(names.Add(scenario.name), $"duplicate scenario name '{scenario.name}'");

            Assert.AreEqual(960, scenario.width, scenario.name);
            Assert.AreEqual(540, scenario.height, scenario.name);
            Assert.AreEqual(24f, scenario.framesPerSecond, scenario.name);

            // A zero- or one-frame loop would make normalizedTime degenerate, so require real motion.
            Assert.Greater(scenario.frameCount, 1, scenario.name);
            Assert.NotNull(scenario.draw, scenario.name);

            // The declared scenarios must satisfy the same output-name rule that ScenarioValidationRejectsUnsafeOutputNames
            // proves is enforced.
            Assert.DoesNotThrow(() => scenario.Validate(), scenario.name);
        }
    }
}
