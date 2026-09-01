namespace ElsaControl.Deployment.Proof;

internal static class DeploymentProofCredentialText
{
    public static int TrimSingleTrailingLineEnding(Span<char> buffer, int length)
    {
        if (length < 0 || length > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (length > 0 && buffer[length - 1] == '\n')
            length--;
        if (length > 0 && buffer[length - 1] == '\r')
            length--;
        return length;
    }
}
