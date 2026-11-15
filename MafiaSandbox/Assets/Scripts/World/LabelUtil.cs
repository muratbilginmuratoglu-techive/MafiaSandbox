using UnityEngine;

namespace MafiaSandbox.World
{
    /// <summary>Measures TextMesh labels (fontSize 64, as every label here uses) so the board behind a sign is as long as its text.</summary>
    public static class LabelUtil
    {
        static Font font;
        /// <summary>World width of text at the given characterSize (TextMesh: 1 font pixel = characterSize x 0.1 m).</summary>
        public static float Width(string text, float charSize, int fontSize = 64)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!font) return text.Length * 0.58f * fontSize * charSize * 0.1f;
            font.RequestCharactersInTexture(text, fontSize, FontStyle.Normal);
            float px = 0f; foreach (char ch in text) { if (font.GetCharacterInfo(ch, out var ci, fontSize, FontStyle.Normal)) px += ci.advance; else px += 0.5f * fontSize; }
            return px * charSize * 0.1f;
        }
        /// <summary>The characterSize to use so the text is at most maxW wide.</summary>
        public static float Fit(string text, float charSize, float maxW) { float w = Width(text, charSize); return w <= maxW || w < 0.01f ? charSize : charSize * maxW / w; }
    }
}
