namespace ExampleAgent.Tests;

public class DotEnvTests
{
    [Fact]
    public void Values_are_set_unless_the_variable_already_exists()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "# a comment\nEXAMPLE_AGENT_TEST_NEW=\"from file\"\nEXAMPLE_AGENT_TEST_SET=from file\n\nnot a pair\n");
        Environment.SetEnvironmentVariable("EXAMPLE_AGENT_TEST_SET", "from environment");

        DotEnv.Load(path);

        Assert.Equal("from file", Environment.GetEnvironmentVariable("EXAMPLE_AGENT_TEST_NEW"));
        Assert.Equal("from environment", Environment.GetEnvironmentVariable("EXAMPLE_AGENT_TEST_SET"));
        File.Delete(path);
    }

    [Fact]
    public void A_missing_file_is_fine() => DotEnv.Load("does-not-exist.env");
}
