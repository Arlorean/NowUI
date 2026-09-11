using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace NowUI.Native.Tests;

public sealed class BrowserNativeAbiTests
{
    [TestCase(typeof(NowUI.Internal.NowLottieNative), "Browser_nowui_vg_blit_mesh", "srcPositions srcColors vertexCount srcIndices indexCount positionScale offsetX offsetY tint4 mask4 rect4 dstVerts dstUvs dstRawUv dstRect dstRadius dstColor dstOutline dstExtra dstMask dstVertexBase dstIndices dstIndexBase indexOffset")]
    [TestCase(typeof(NowUI.Internal.NowLottieNative), "Browser_nowui_vg_blit_text_run", "glyphs start end x y fontSize baseline mask4 color4 outline4 outline pixelRange dstVerts dstUvs dstRawUv dstRect dstRadius dstColor dstOutline dstExtra dstMask dstVertexBase dstIndices dstIndexBase outPenX outCounts outBounds")]
    [TestCase(typeof(NowUI.Internal.NowLottieNative), "Browser_nowui_vg_pack_canvas", "srcVerts srcUvs srcRadius srcRawUv srcColors srcRect srcMask srcExtra srcOutline vertexCount isText offsetX offsetY dst dstVertexBase")]
    [TestCase(typeof(NowUI.Internal.NowLottieNative), "Browser_nowui_vg_pack_render", "srcVerts srcUvs srcRadius srcRawUv srcColors srcRect srcMask srcExtra srcOutline vertexCount offsetX offsetY dst dstVertexBase")]
    [TestCase(typeof(NowUI.Internal.NowLottieNative), "Browser_nowui_vg_stroke", "contours contourFloatCount contourCount clipContours clipFloatCount clipContourCount clipInvert paint paintFloatCount width cap join hasTrim trimStart trimEnd trimOffset trimIndividual")]
    [TestCase(typeof(NowUI.NowFontCompiler), "Browser_nowui_compile_font_from_memory_with_codepoints", "fontData fontDataLength size pixelRange codepoints codepointCount atlasRgba atlasRgbaLength glyphs glyphCapacity info errorBuffer errorBufferLength")]
    public void CompactArgumentsPreserveNativeSequentialOffsets(Type owner, string name, string fieldNames)
    {
        var type = owner.GetNestedType(name, BindingFlags.NonPublic)!;
        Assert.That(type, Is.Not.Null);
        Assert.That(type.StructLayoutAttribute!.Value, Is.EqualTo(LayoutKind.Sequential));
        int offset = 0;
        foreach (string fieldName in fieldNames.Split(' '))
        {
            var field = type.GetField(fieldName)!;
            Assert.That(field, Is.Not.Null);
            int size = field.FieldType.IsPointer ? IntPtr.Size : sizeof(int);
            Assert.That(field.FieldType.IsPointer || field.FieldType == typeof(int) || field.FieldType == typeof(float), Is.True);
            offset = (offset + size - 1) & ~(size - 1);
            Assert.That(Marshal.OffsetOf(type, fieldName).ToInt32(), Is.EqualTo(offset), name + "." + fieldName);
            offset += size;
        }
        int sizeOfStruct = (offset + IntPtr.Size - 1) & ~(IntPtr.Size - 1);
        Assert.That(Marshal.SizeOf(type), Is.EqualTo(sizeOfStruct));
        var entry = owner.GetMethod(name.Replace("Browser_", "") + "_browser", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.That(entry.GetParameters(), Has.Length.EqualTo(1));
        Assert.That(entry.GetParameters()[0].ParameterType, Is.EqualTo(type.MakePointerType()));
    }
}
