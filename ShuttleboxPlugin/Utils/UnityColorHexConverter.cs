using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace ShuttleboxPlugin.Utils;
internal class UnityColorHexConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hex = reader.GetString();
        return ColorExt.Hex(hex);
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(ColorExt.ToHex(value));
    }
}
