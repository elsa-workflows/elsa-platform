using ElsaControl.Deployment.ProofHost;

return await ProofHostApplication.RunAsync(
    args,
    ProofHostOptionsParser.ReadProcessEnvironment(),
    new AzureProofHostExecutor(Console.Out, Console.Error));
