using System.Text.Json;
using System.Text.Json.Serialization;
using Ambrosio.McpServer.Mcp.Tools;

namespace Ambrosio.McpServer.Mcp.Files;

/// <summary>
/// Lê o argumento de arquivo nos dois formatos que o descritor publica: o objeto do
/// <c>openai/fileParams</c> (<c>download_url</c>, <c>file_id</c>, <c>mime_type</c>, <c>file_name</c>) ou
/// um <b>texto</b> — o caminho do arquivo no ChatGPT (<c>/mnt/data/...</c>, imagem gerada na conversa) ou
/// um link https.
/// <para>
/// O texto existe para a imagem gerada: o modelo escreve o caminho no campo e, se o ChatGPT o converter
/// em objeto com link, a ferramenta recebe o objeto. Se chegar cru, vira o <c>download_url</c> e o
/// <see cref="SafeFileDownloader"/> responde com a mensagem própria de caminho interno, em vez de a
/// desserialização falhar com erro genérico.
/// </para>
/// </summary>
public sealed class FileArgumentConverter : JsonConverter<FileArgument>
{
    public override FileArgument? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return new FileArgument { DownloadUrl = reader.GetString() };

            case JsonTokenType.StartObject:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    var root = document.RootElement;

                    return new FileArgument
                    {
                        DownloadUrl = Text(root, "download_url"),
                        FileId = Text(root, "file_id"),
                        MimeType = Text(root, "mime_type"),
                        FileName = Text(root, "file_name")
                    };
                }

            default:
                throw new JsonException("O arquivo deve ser um objeto com download_url ou o caminho do arquivo.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FileArgument value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("download_url", value.DownloadUrl);
        writer.WriteString("file_id", value.FileId);
        writer.WriteString("mime_type", value.MimeType);
        writer.WriteString("file_name", value.FileName);
        writer.WriteEndObject();
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
