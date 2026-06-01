using FluentAssertions;

namespace RimBob.Tests.Food;

public sealed class FoodFixtureTests
{
    [Fact]
    public void FixtureDirectory_ContainsSixM3Scenarios()
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Src", "Tests", "Food", "Fixtures");
            if (Directory.Exists(candidate))
            {
                Directory.GetFiles(candidate, "*.json").Should().HaveCount(6);
                return;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find Food fixture directory.");
    }
}
