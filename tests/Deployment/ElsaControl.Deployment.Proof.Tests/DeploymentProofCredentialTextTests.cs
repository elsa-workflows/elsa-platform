using ElsaControl.Deployment.Proof;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class DeploymentProofCredentialTextTests
{
    [Theory]
    [InlineData("proof-password", "proof-password")]
    [InlineData("proof-password\n", "proof-password")]
    [InlineData("proof-password\r\n", "proof-password")]
    [InlineData(" proof-password \n", " proof-password ")]
    [InlineData("proof-password\n\n", "proof-password\n")]
    public void Trims_only_one_trailing_line_ending(string input, string expected)
    {
        var buffer = input.ToCharArray();

        var length = DeploymentProofCredentialText.TrimSingleTrailingLineEnding(buffer, buffer.Length);

        Assert.Equal(expected, new string(buffer, 0, length));
    }
}
