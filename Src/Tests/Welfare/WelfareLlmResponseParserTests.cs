using FluentAssertions;
using RimBob.Core.Advice;
using RimBob.Core.Briefings;
using RimBob.LLM;

namespace RimBob.Tests.Welfare;

public sealed class WelfareLlmResponseParserTests
{
    [Fact]
    public void Parse_NormalizesAdviceFlagsAndStripsApplyActions()
    {
        const string raw = """
        {
          "summary": "Social pressure is driving mood risk.",
          "advice": [
            {
              "title": "Separate social fighters",
              "body": "Alice has a social fight mood penalty and needs direct player review.",
              "rationale": "The dominant thought is social, not a deterministic room or recreation fix.",
              "actions": [
                {
                  "kind": "place_blueprint",
                  "instruction": "Build a replacement bedroom for Alice."
                }
              ],
              "guide_citations": []
            }
          ],
          "flags": [
            {
              "id": "welfare:dining_support",
              "source_minister": "Welfare",
              "priority": "medium",
              "domain": "welfare",
              "summary": "Dining setup can reduce mood pressure.",
              "building_requests": [
                {
                  "request": "starter dining table",
                  "reason": "ate without table mood pressure",
                  "target_class": "table",
                  "room_class": "dining",
                  "requested_from": "Willie"
                }
              ]
            },
            {
              "building_requests": [
                {
                  "request": "malformed request without flag envelope",
                  "reason": "missing flag id and priority",
                  "target_class": "table"
                }
              ]
            }
          ],
          "notes": "social_pressure"
        }
        """;

        WelfareLlmParseResult result = WelfareLlmResponseParser.Parse(raw, Briefing(), []);

        result.ParseMode.Should().Be("tolerant_normalization");
        result.Normalized.Should().BeTrue();
        result.DroppedFlagCount.Should().Be(1);
        AdviceItem advice = result.Response.Advice.Should().ContainSingle().Subject;
        advice.Priority.Should().Be(Priority.Medium);
        advice.Minister.Should().Be("Welfare");
        advice.Id.Should().Be("welfare_llm_300000_1");
        advice.Actions.Should().ContainSingle().Which.Kind.Should().Be(AdviceActionKind.Note);
        advice.Actions.Single().Owner.Should().Be("Welfare");
        advice.Actions.Single().Apply.Should().BeNull();
        result.Response.Flags.Should().ContainSingle().Which.BuildingRequests.Should().ContainSingle()
            .Which.RequestedFrom.Should().Be("Willie");
    }

    private static WelfareSourceBriefing Briefing() => new(
        BriefingVersion: 7,
        GameTick: 300_000,
        ColonistCount: 3,
        Mood: new WelfareMoodSummary(0.58f, BreakRiskCount: 0, StressedCount: 1, ContentCount: 1),
        WorstPawns:
        [
            new WelfarePawnMood(
                "p1",
                "Alice",
                0.46f,
                Sleep: 0.7f,
                Comfort: 0.7f,
                Beauty: 0.7f,
                Joy: 0.7f,
                FreshAir: 0.7f,
                DrugsDesire: 0f,
                TopNegativeThoughts:
                [
                    new WelfareMoodThought("SocialFight", "social fight", -5f, 0)
                ])
        ],
        NeedLows: [],
        Rooms: new WelfareRoomSummary(1, 1, 0, 30f, []),
        Sleep: new WelfareSleepSummary(3, 3, 0, 0),
        Recreation: new WelfareRecreationSummary(0, 0, 0, false),
        ThoughtDigest: new WelfareThoughtDigest(
        [
            new WelfareThoughtGroup(ThoughtCategory.Social, 1, -5f, "social fight")
        ]),
        DataCoverage: new WelfareDataCoverage(true, true, true, true, false));
}
