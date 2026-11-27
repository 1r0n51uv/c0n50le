using System.Text;
using C0n50le.Core.Translation;
using Xunit.Abstractions;

namespace C0n50le.Tests;

public sealed class TranslationTests(ITestOutputHelper output)
{
    private static UnigramTokenizer Tiny() => UnigramTokenizer.Parse(new MemoryStream(Encoding.UTF8.GetBytes("""
        {"model":{"type":"Unigram","unk_id":1,"vocab":[["</s>",0],["<unk>",0],["▁the",-2],["▁game",-3],["▁ga",-4],["me",-4],["▁",-5],["s",-3],["<pad>",0]]}}
        """)));

    [Fact]
    public void Words_are_cut_into_the_best_scoring_pieces()
    {
        var tokenizer = Tiny();
        // "▁game" beats "▁ga" + "me"; "games" needs "▁game" + "s"; the end of the text is </s>.
        Assert.Equal([2L, 3L, 3L, 7L, 0L], tokenizer.Encode("the game games"));
        Assert.Equal("the game games", tokenizer.Decode([2, 3, 3, 7, 0]));
    }

    [Fact]
    public void Unknown_characters_become_one_unknown_piece()
    {
        var ids = Tiny().Encode("the ŽŽ");
        Assert.Equal([2L, 6L, 1L, 0L], ids);
    }

    [Fact]
    public void Long_text_is_cut_at_sentence_ends()
    {
        var text = string.Join(" ", Enumerable.Range(1, 12).Select(index => $"This is sentence number {index} of the text."));
        var chunks = OfflineTranslator.Chunks(text, 120).ToList();
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.EndsWith(".", chunk));
        Assert.Equal(text, string.Join(" ", chunks));
    }

    /// <summary>Runs only where the model has been downloaded (C0N50LE_TRANSLATION_MODEL points at its folder).</summary>
    [Fact]
    public void The_real_model_translates_a_game_description()
    {
        var folder = Environment.GetEnvironmentVariable("C0N50LE_TRANSLATION_MODEL");
        if (folder is null || !TranslationModel.IsInstalled(folder)) return;
        using var translator = new OfflineTranslator(folder);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var italian = translator.Translate(
            "Shape the world and build a cozy new life with Pokémon. Play as a Ditto that has transformed to look like a human! " +
            "Find your Pokémon friends and restore the land together.");
        output.WriteLine($"{watch.ElapsedMilliseconds} ms: {italian}");
        Assert.Contains("Pokémon", italian);
        Assert.Matches(@"\b(il|la|un|una|con|e)\b", italian);
    }
}
