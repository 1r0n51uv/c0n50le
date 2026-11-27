using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace C0n50le.Core.Translation;

/// <summary>
/// English to Italian on this PC, with no service: the Opus-MT en-it model (Helsinki-NLP, Apache-2.0) in ONNX, run by
/// ONNX Runtime. Text is cut into sentences and each is translated greedily, one token at a time.
/// </summary>
public sealed class OfflineTranslator : IDisposable
{
    private readonly UnigramTokenizer _tokenizer;
    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly long _startId;
    private readonly long _padId;
    private readonly object _gate = new();

    public OfflineTranslator(string folder)
    {
        _tokenizer = UnigramTokenizer.Load(Path.Combine(folder, TranslationModel.Tokenizer));
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8)
        };
        _encoder = new InferenceSession(Path.Combine(folder, TranslationModel.Encoder), options);
        _decoder = new InferenceSession(Path.Combine(folder, TranslationModel.Decoder), options);
        // Marian starts decoding from the pad token, which must never be produced.
        _startId = _padId = _tokenizer.VocabularySize - 1;
    }

    /// <summary>The Italian text, sentence by sentence; blank in, blank out.</summary>
    public string Translate(string english, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(english)) return "";
        var output = new StringBuilder();
        foreach (var chunk in Chunks(english))
        {
            token.ThrowIfCancellationRequested();
            var italian = Finish(chunk, TranslateChunk(chunk, token));
            if (italian.Length == 0) continue;
            if (output.Length > 0) output.Append(' ');
            output.Append(italian);
        }
        return output.ToString();
    }

    /// <summary>The source sentence's closing mark, when the model dropped it.</summary>
    private static string Finish(string source, string translated)
    {
        translated = translated.Trim();
        if (translated.Length == 0) return "";
        var end = source.TrimEnd()[^1];
        return end is '.' or '!' or '?' or '…' && !".!?…\"'»)".Contains(translated[^1]) ? translated + end : translated;
    }

    /// <summary>Sentences, grouped while short, so each pass stays small and fast.</summary>
    public static IEnumerable<string> Chunks(string text, int size = 1)
    {
        var sentences = Regex.Split(Regex.Replace(text, @"\s+", " ").Trim(), @"(?<=[.!?…])\s+(?=[\p{Lu}\p{N}""'(\[])");
        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            if (current.Length > 0 && current.Length + sentence.Length + 1 > size)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private string TranslateChunk(string text, CancellationToken token)
    {
        var source = _tokenizer.Encode(text);
        if (source.Count > 500) source = [.. source.Take(499), _tokenizer.EndId];
        var length = source.Count;
        var ids = new DenseTensor<long>(source.ToArray(), [1, length]);
        var mask = new DenseTensor<long>(Enumerable.Repeat(1L, length).ToArray(), [1, length]);

        lock (_gate)
        {
            using var encoded = _encoder.Run(
            [
                NamedOnnxValue.CreateFromTensor(Name(_encoder, "input_ids"), ids),
                NamedOnnxValue.CreateFromTensor(Name(_encoder, "attention_mask"), mask)
            ]);
            var hidden = encoded.First().AsTensor<float>().ToDenseTensor();

            var output = new List<long> { _startId };
            var limit = Math.Min(512, length * 2 + 16);
            while (output.Count < limit)
            {
                token.ThrowIfCancellationRequested();
                var input = new DenseTensor<long>(output.ToArray(), [1, output.Count]);
                using var decoded = _decoder.Run(
                [
                    NamedOnnxValue.CreateFromTensor(Name(_decoder, "input_ids"), input),
                    NamedOnnxValue.CreateFromTensor(Name(_decoder, "encoder_attention_mask"), mask),
                    NamedOnnxValue.CreateFromTensor(Name(_decoder, "encoder_hidden_states"), hidden)
                ]);
                var logits = decoded.First(value => value.Name.Contains("logits")).AsTensor<float>();
                var vocabulary = logits.Dimensions[2];
                var last = output.Count - 1;
                long next = -1;
                var bestScore = float.NegativeInfinity;
                for (var id = 0; id < vocabulary; id++)
                {
                    if (id == _padId) continue;
                    var score = logits[0, last, id];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        next = id;
                    }
                }
                if (next == _tokenizer.EndId || next < 0) break;
                output.Add(next);
                if (Looping(output)) break;
            }
            return _tokenizer.Decode(output.Skip(1), new HashSet<long> { _padId });
        }
    }

    /// <summary>A greedy decoder can fall into repeating the same few tokens: stop when the last eight repeat.</summary>
    private static bool Looping(List<long> output)
    {
        const int span = 4;
        if (output.Count < span * 3) return false;
        for (var index = 0; index < span; index++)
            if (output[^(index + 1)] != output[^(index + 1 + span)] || output[^(index + 1)] != output[^(index + 1 + span * 2)]) return false;
        output.RemoveRange(output.Count - span * 2, span * 2);
        return true;
    }

    private static string Name(InferenceSession session, string wanted) =>
        session.InputMetadata.Keys.FirstOrDefault(name => name == wanted)
        ?? session.InputMetadata.Keys.First(name => name.Contains(wanted, StringComparison.Ordinal));

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
    }
}

/// <summary>Where the translation model comes from and what it is made of.</summary>
public static class TranslationModel
{
    public const string Encoder = "encoder_model_quantized.onnx";
    public const string Decoder = "decoder_model_quantized.onnx";
    public const string Tokenizer = "tokenizer.json";
    public const string Name = "Opus-MT en-it";
    public const string Source = "https://huggingface.co/Xenova/opus-mt-en-it/resolve/main/";

    /// <summary>The files to download (path in the repository, name on disk) and roughly how big they are together.</summary>
    public static IReadOnlyList<(string Remote, string Local)> Files { get; } =
    [
        ("tokenizer.json", Tokenizer),
        ("onnx/encoder_model_quantized.onnx", Encoder),
        ("onnx/decoder_model_quantized.onnx", Decoder)
    ];

    public const long ApproximateBytes = 136_000_000;

    public static bool IsInstalled(string folder) => Files.All(file => File.Exists(Path.Combine(folder, file.Local)));
}
