using FluentAssertions;

namespace RimAI.Tests.Food;

public sealed class FoodFixtureTests
{
    [Fact]
    public void FixtureDirectory_ContainsFiveM3Scenarios()
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Src", "Tests", "Food", "Fixtures");
            if (Directory.Exists(candidate))
            {
                Directory.GetFiles(candidate, "*.json").Should().HaveCount(5);
                return;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find Food fixture directory.");
    }
}
