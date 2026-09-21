using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace OhMyHarness.Core;

public static class LocalEmbeddings
{
    public const string ModelName = "paraphrase-multilingual-MiniLM-L12-v2";
    // Change the identity whenever the weights or tokenization change: old vectors are incompatible.
    public const string ModelId = "minilm-multilingual-l12-v2-66fc00f5-sp-v1";
    static readonly Lazy<(InferenceSession Session, SentencePieceTokenizer Tokenizer)> Model = new(Load);
    static (InferenceSession, SentencePieceTokenizer) Load()
    {
        var folder = Path.Combine(PortableStorage.Root, "models", "minilm_multilingual"); Directory.CreateDirectory(folder);
        foreach (var resource in typeof(LocalEmbeddings).Assembly.GetManifestResourceNames().Where(x => x.Contains(".Models.minilm_multilingual.")))
        {
            var file = Path.Combine(folder, resource.Split(".Models.minilm_multilingual.")[1]);
            using var source = typeof(LocalEmbeddings).Assembly.GetManifestResourceStream(resource)!;
            if (!File.Exists(file) || new FileInfo(file).Length != source.Length) { using var target = File.Create(file); source.CopyTo(target); }
        }
        using var options = new SessionOptions { InterOpNumThreads = 1, IntraOpNumThreads = 2 };
        using var tokenizerModel = File.OpenRead(Path.Combine(folder, "sentencepiece.bpe.model"));
        return (new InferenceSession(Path.Combine(folder, "model.onnx"), options), SentencePieceTokenizer.Create(tokenizerModel, false, false));
    }
    public static long[] Tokenize(string text)
    {
        // XLM-R vocabulary uses SentencePiece ids + 1, except <unk> (0 -> 3).
        // Preserve case and accents; SentencePiece applies the model's own normalization.
        // Match Sentence Transformers' 128-token window, reserving BOS/EOS.
        return [0, .. Model.Value.Tokenizer.EncodeToIds(text, false, false, 126, out _, out _).Select(id => id == 0 ? 3L : id + 1L), 2];
    }
    public static float[] Embed(string text)
    {
        var session = Model.Value.Session;
        var ids = Tokenize(text);
        var shape = new[] { 1, ids.Length };
        var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids.ToArray(), shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), shape)) };
        if (session.InputMetadata.ContainsKey("token_type_ids")) inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[ids.Length], shape)));
        using var results = session.Run(inputs);
        var tensor = results.First().AsTensor<float>(); int size = tensor.Dimensions[2]; var vector = new float[size];
        for (int i = 0; i < ids.Length; i++) for (int j = 0; j < size; j++) vector[j] += tensor[0,i,j] / ids.Length;
        Normalize(vector); return vector;
    }
    public static void Normalize(float[] vector)
    {
        var norm = Math.Sqrt(vector.Sum(x => (double)x*x));
        if (!double.IsFinite(norm) || norm == 0) throw new InvalidOperationException("Invalid embedding vector.");
        for (int i = 0; i < vector.Length; i++) vector[i] /= (float)norm;
    }
}
