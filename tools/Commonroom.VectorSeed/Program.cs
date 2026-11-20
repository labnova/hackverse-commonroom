using Azure.AI.Projects;
using Azure.Identity;
using OpenAI.Files;
using OpenAI.VectorStores;

// Usage: Commonroom.VectorSeed <docs-folder> <name>
// Requires FOUNDRY_PROJECT_ENDPOINT (or Foundry__ProjectEndpoint) and an `az login` session (DefaultAzureCredential).
string[] allowedExtensions = [".pdf", ".md", ".txt", ".docx"];

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Commonroom.VectorSeed <docs-folder> <name>");
    Console.Error.WriteLine("Env:   FOUNDRY_PROJECT_ENDPOINT=https://<resource>.services.ai.azure.com/api/projects/<project>");
    return 2;
}

var folder = args[0];
var storeName = args[1];
var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
               ?? Environment.GetEnvironmentVariable("Foundry__ProjectEndpoint");

if (string.IsNullOrWhiteSpace(endpoint))
{
    Console.Error.WriteLine("FOUNDRY_PROJECT_ENDPOINT is not set.");
    return 2;
}
if (!Directory.Exists(folder))
{
    Console.Error.WriteLine($"Folder not found: {folder}");
    return 2;
}

var files = Directory.EnumerateFiles(folder)
    .Where(f => allowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .ToList();
if (files.Count == 0)
{
    Console.Error.WriteLine($"No {string.Join(' ', allowedExtensions)} files in {folder}");
    return 2;
}

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
var ct = cts.Token;

var project = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
var fileClient = project.ProjectOpenAIClient.GetOpenAIFileClient();
var storeClient = project.ProjectOpenAIClient.GetProjectVectorStoresClient();

VectorStore store = (await storeClient.CreateVectorStoreAsync(new VectorStoreCreationOptions { Name = storeName }, ct)).Value;
Console.WriteLine($"Created vector store '{storeName}': {store.Id}");

var pending = new Dictionary<string, string>(StringComparer.Ordinal);
var ok = 0;
var failed = 0;

foreach (var path in files)
{
    var fileName = Path.GetFileName(path);
    try
    {
        await using var stream = File.OpenRead(path);
        OpenAIFile uploaded = (await fileClient.UploadFileAsync(stream, fileName, FileUploadPurpose.Assistants, ct)).Value;
        await storeClient.AddFileToVectorStoreAsync(store.Id, uploaded.Id, ct);
        pending[uploaded.Id] = fileName;
        Console.WriteLine($"  uploaded {fileName} -> {uploaded.Id}");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        failed++;
        Console.Error.WriteLine($"  FAILED upload {fileName}: {ex.Message}");
    }
}

Console.WriteLine("Waiting for indexing...");
while (pending.Count > 0)
{
    await Task.Delay(TimeSpan.FromSeconds(3), ct);
    foreach (var (fileId, fileName) in pending.ToList())
    {
        VectorStoreFile vsFile = (await storeClient.GetVectorStoreFileAsync(store.Id, fileId, ct)).Value;
        if (vsFile.Status == VectorStoreFileStatus.Completed)
        {
            ok++;
            pending.Remove(fileId);
            Console.WriteLine($"  indexed {fileName}");
        }
        else if (vsFile.Status is VectorStoreFileStatus.Failed or VectorStoreFileStatus.Cancelled)
        {
            failed++;
            pending.Remove(fileId);
            Console.Error.WriteLine($"  FAILED indexing {fileName}: {vsFile.LastError?.Message ?? vsFile.Status.ToString()}");
        }
    }
}

Console.WriteLine($"vector_store_id={store.Id}");
Console.WriteLine($"files ok={ok} failed={failed}");
return failed == 0 ? 0 : 1;
