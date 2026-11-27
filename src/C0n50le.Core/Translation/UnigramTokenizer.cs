using System.Text;
using System.Text.Json;

namespace C0n50le.Core.Translation;

/// <summary>
/// The SentencePiece Unigram tokenizer of a Marian (Opus-MT) model, read from its tokenizer.json: words are split on
/// whitespace, each gets the "▁" prefix, and is cut into the pieces with the best total score (Viterbi).
/// </summary>
public sealed class UnigramTokenizer
{
    public const char Space = '▁';

    private readonly Dictionary<string, (int Id, double Score)> _pieces = new(StringComparer.Ordinal);
    private readonly string[] _byId;
    private readonly int _longest;

    public int UnknownId { get; }
    public int EndId { get; }

    private UnigramTokenizer(List<(string Piece, double Score)> vocab, int unknownId)
    {
        _byId = new string[vocab.Count];
        for (var id = 0; id < vocab.Count; id++)
        {
            var (piece, score) = vocab[id];
            _byId[id] = piece;
            _pieces.TryAdd(piece, (id, score));
            _longest = Math.Max(_longest, piece.Length);
        }
        UnknownId = unknownId;
        EndId = _pieces.TryGetValue("</s>", out var end) ? end.Id : 0;
    }

    public int VocabularySize => _byId.Length;

    public static UnigramTokenizer Load(string tokenizerJson)
    {
        using var stream = File.OpenRead(tokenizerJson);
        return Parse(stream);
    }

    public static UnigramTokenizer Parse(Stream json)
    {
        using var document = JsonDocument.Parse(json);
        var model = document.RootElement.GetProperty("model");
        if (model.GetProperty("type").GetString() != "Unigram") throw new InvalidDataException("Not a Unigram tokenizer.");
        var vocab = new List<(string, double)>();
        foreach (var entry in model.GetProperty("vocab").EnumerateArray())
            vocab.Add((entry[0].GetString() ?? "", entry[1].GetDouble()));
        var unknown = model.TryGetProperty("unk_id", out var unk) && unk.ValueKind == JsonValueKind.Number ? unk.GetInt32() : 1;
        return new UnigramTokenizer(vocab, unknown);
    }

    /// <summary>The ids of <paramref name="text"/>, ending with &lt;/s&gt;, as the encoder wants them.</summary>
    public List<long> Encode(string text)
    {
        var ids = new List<long>();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            EncodeWord(Space + word, ids);
        ids.Add(EndId);
        return ids;
    }

    private void EncodeWord(string word, List<long> ids)
    {
        // best[i]: the best score of a split of word[..i]; from[i]: where its last piece starts and its id.
        var best = new double[word.Length + 1];
        var start = new int[word.Length + 1];
        var piece = new int[word.Length + 1];
        Array.Fill(best, double.NegativeInfinity);
        best[0] = 0;
        for (var end = 1; end <= word.Length; end++)
        {
            for (var begin = Math.Max(0, end - _longest); begin < end; begin++)
            {
                if (double.IsNegativeInfinity(best[begin])) continue;
                if (!_pieces.TryGetValue(word[begin..end], out var found)) continue;
                var score = best[begin] + found.Score;
                if (score <= best[end]) continue;
                best[end] = score;
                start[end] = begin;
                piece[end] = found.Id;
            }
            // A character no piece covers becomes <unk>, with a heavy penalty, so the rest still splits well.
            if (double.IsNegativeInfinity(best[end]) && !double.IsNegativeInfinity(best[end - 1]))
            {
                best[end] = best[end - 1] - 20;
                start[end] = end - 1;
                piece[end] = UnknownId;
            }
        }
        var stack = new Stack<int>();
        for (var at = word.Length; at > 0; at = start[at]) stack.Push(piece[at]);
        // Neighbouring unknowns collapse into one, as SentencePiece does.
        var last = -1;
        foreach (var id in stack)
        {
            if (id == UnknownId && last == UnknownId) continue;
            ids.Add(id);
            last = id;
        }
    }

    /// <summary>Text from output ids: special tokens dropped, "▁" back to spaces.</summary>
    public string Decode(IEnumerable<long> ids, ISet<long>? skip = null)
    {
        var text = new StringBuilder();
        foreach (var id in ids)
        {
            if (id < 0 || id >= _byId.Length || id == EndId || id == UnknownId || skip?.Contains(id) == true) continue;
            text.Append(_byId[id]);
        }
        return text.Replace(Space, ' ').ToString().Trim();
    }
}
