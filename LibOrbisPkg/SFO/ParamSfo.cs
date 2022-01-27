﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LibOrbisPkg.Util;

namespace LibOrbisPkg.SFO
{
  public class ParamSfo
  {
    /// <summary>
    /// Array index access to the SFO file.
    /// Setting a value to null removes it from the SFO.
    /// When getting a value, if a value with the given key doesn't exist, null is returned.
    /// If the name of the value doesn't match the name given to the indexer, the value's name is updated.
    /// </summary>
    /// <param name="name">The key of the entry to get/set</param>
    /// <returns>The value or null if it doesn't exist</returns>
    public Value this[string name]
    {
      get { return GetValueByName(name); }
      set
      {
        if (GetValueByName(name) is Value v)
        {
          Values.Remove(v);
        }
        if(value != null)
        {
          if(value.Name != name)
          {
            value.Name = name;
          }
          Values.Add(value);
          Values.Sort((v1, v2) => v1.Name.CompareTo(v2.Name));
        }
      }
    }

    /// <summary>
    /// Sets or updates an entry with the given key name.
    /// </summary>
    public Value SetValue(string key, SfoEntryType type, string @value, int maxLength = 4)
    {
      var v = Value.Create(key, type, @value, maxLength);
      this[key] = v;
      return v;
    }

    public List<Value> Values;
    public ParamSfo()
    {
      Values = new List<Value>();
    }
    public Value GetValueByName(string name)
    {
      foreach(var v in Values)
      {
        if (v.Name == name) return v;
      }
      return null;
    }

    public static ParamSfo FromStream(Stream s)
    {
      var ret = new ParamSfo();
      var start = s.Position;
      if (s.ReadUInt32BE() == 0x53434543)
      {
        start = start + 0x800;
      }
      s.Position = start;
      if (s.ReadUInt32BE() != 0x00505346)
      {
        throw new InvalidDataException("File is missing SFO magic");
      }
      s.Position = start + 8;
      var keyTableStart = s.ReadInt32LE();
      var dataTableStart = s.ReadInt32LE();
      var numValues = s.ReadInt32LE();
      for(int value = 0; value < numValues; value++)
      {
        s.Position = value * 0x10 + 0x14 + start;
        var keyOffset = s.ReadUInt16LE();
        var format = (SfoEntryType)s.ReadUInt16LE();
        var len = s.ReadInt32LE();
        var maxLen = s.ReadInt32LE();
        var dataOffset = s.ReadUInt32LE();
        s.Position = start + keyTableStart + keyOffset;
        var name = s.ReadASCIINullTerminated();
        s.Position = start + dataTableStart + dataOffset;
        switch(format)
        {
          case SfoEntryType.Integer:
            ret.Values.Add(new IntegerValue(name, s.ReadInt32LE()));
            break;
          case SfoEntryType.Utf8:
            ret.Values.Add(new Utf8Value(name, Encoding.UTF8.GetString(s.ReadBytes(len > 0 ? len - 1 : len)), maxLen));
            break;
          case SfoEntryType.Utf8Special:
            ret.Values.Add(new Utf8SpecialValue(name, Encoding.UTF8.GetString(s.ReadBytes(len)), maxLen));
            break;
          default:
            throw new Exception($"Unknown SFO type: {(ushort)format:X4}");
        }
      }
      return ret;
    }
    int keyTableOffset => 0x14 + (Values.Count * 0x10);

    public int FileSize => CalcSize().Item2;
    
    /// <summary>
    /// 
    /// </summary>
    /// <returns>A tuple containing the offset of the data table and the total file size.</returns>
    private Tuple<int, int> CalcSize()
    {
      int keyTableSize = 0x0;
      int dataSize = 0x0;
      Values.Sort((v1, v2) => v1.Name.CompareTo(v2.Name));
      foreach (var v in Values)
      {
        keyTableSize += v.Name.Length + 1;
        dataSize += v.MaxLength;
      }
      int dataTableOffset = keyTableOffset + keyTableSize;
      if (dataTableOffset % 4 != 0) dataTableOffset += 4 - (dataTableOffset % 4);
      return Tuple.Create(dataTableOffset, dataSize + dataTableOffset);
    }

    public void Write(Stream s)
    {
      var size = CalcSize();
      var dataTableOffset = size.Item1;
      var fileSize = size.Item2;
      s.SetLength(0);
      s.SetLength(fileSize);
      s.WriteInt32BE(0x00505346); // " PSF" magic
      s.WriteInt32LE(0x101); // Version?
      s.WriteInt32LE(keyTableOffset);
      s.WriteInt32LE(dataTableOffset);
      s.WriteInt32LE(Values.Count);
      int keyOffset = 0, dataOffset = 0, index = 0;
      foreach (var v in Values)
      {
        s.Position = 0x14 + 0x10 * index++;
        s.WriteUInt16LE((ushort)keyOffset);
        s.WriteUInt16LE((ushort)v.Type);
        s.WriteInt32LE(v.Length);
        s.WriteInt32LE(v.MaxLength);
        s.WriteInt32LE(dataOffset);
        s.Position = keyTableOffset + keyOffset;
        s.Write(Encoding.ASCII.GetBytes(v.Name), 0, v.Name.Length);
        s.WriteByte(0);
        s.Position = dataTableOffset + dataOffset;
        var val = v.ToByteArray();
        s.Write(val, 0, val.Length);
        keyOffset += v.Name.Length + 1;
        dataOffset += v.MaxLength;
      }
    }
    public byte[] Serialize()
    {
      using (var s = new MemoryStream())
      {
        Write(s);
        return s.ToArray();
      }
    }
    public static ParamSfo Deserialize(byte[] file)
    {
      using (var ms = new MemoryStream(file))
        return FromStream(ms);
    }
    public static ParamSfo DefaultAC = new ParamSfo()
    {
      Values = new List<Value>
      {
        new IntegerValue("ATTRIBUTE", 0),
        new Utf8Value("CATEGORY", "ac", 4),
        new Utf8Value("CONTENT_ID", "AA0000-BBBB00000_00-ZZZZZZZZZZZZZZZZ", 48),
        new Utf8Value("FORMAT", "obs", 4),
        new Utf8Value("TITLE", "Title", 128),
        new Utf8Value("TITLE_ID", "BBBB00000", 12),
        new Utf8Value("VERSION", "01.00", 8),
      }
    };
    public static ParamSfo DefaultGD = new ParamSfo()
    {
      Values = new List<Value>
      {
        new IntegerValue("APP_TYPE", 4),
        new Utf8Value("APP_VER", "01.00", 8),
        new IntegerValue("ATTRIBUTE", 0),
        new Utf8Value("CATEGORY", "gd", 4),
        new Utf8Value("CONTENT_ID", "AA0000-BBBB00000_00-ZZZZZZZZZZZZZZZZ", 48),
        new IntegerValue("DOWNLOAD_DATA_SIZE", 0),
        new Utf8Value("FORMAT", "obs", 4),
        new IntegerValue("PARENTAL_LEVEL", 1),
        new Utf8Value("TITLE", "Title", 128),
        new Utf8Value("TITLE_ID", "BBBB00000", 12),
        new IntegerValue("SYSTEM_VER", 0),
        new Utf8Value("VERSION", "01.00", 8),
      }
    };
  }

  public enum SfoEntryType : ushort
  {
    Utf8Special = 0x4,
    Utf8 = 0x204,
    Integer = 0x404
  };
  public abstract class Value
  {
    public Value(string name, SfoEntryType type)
    {
      Name = name; Type = type;
    }
    public SfoEntryType Type;
    public string Name;
    public abstract int Length { get; }
    public abstract int MaxLength { get; }
    public abstract byte[] ToByteArray();

    public static Value Create(string name, SfoEntryType type, string value, int maxLength = 4)
    {
      switch (type)
      {
        case SfoEntryType.Utf8Special:
          return new Utf8SpecialValue(name, value, maxLength);
        case SfoEntryType.Utf8:
          return new Utf8Value(name, value, maxLength);
        case SfoEntryType.Integer:
          int newNumber = 0;
          if (value.Contains("0x"))
            int.TryParse(value.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out newNumber);
          else
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out newNumber);
          return new IntegerValue(name, newNumber);
        default:
          return null;
      }
      
    }
  }
  /// <summary>
  /// A special string used in system SFOs? Not null terminated according to PS3 DevWiki.
  /// </summary>
  public class Utf8SpecialValue : Value
  {
    public Utf8SpecialValue(string name, string value, int maxLength) 
      : base(name, SfoEntryType.Utf8Special)
    {
      Type = SfoEntryType.Utf8Special;
      MaxLength = maxLength;
      Value = value;
    }
    public string Value;
    public override int Length => Value.Length;
    public override int MaxLength { get; }
    public override byte[] ToByteArray() => Encoding.UTF8.GetBytes(Value);
    public override string ToString()
    {
      return Value;
    }
  }
  /// <summary>
  /// A utf8-encoded string value. Null terminated.
  /// </summary>
  public class Utf8Value : Value
  {
    public Utf8Value(string name, string value, int maxLength)
      : base(name, SfoEntryType.Utf8)
    {
      Type = SfoEntryType.Utf8;
      MaxLength = maxLength;
      Value = value;
    }
    public override int Length => Encoding.UTF8.GetByteCount(Value) + 1;
    public override int MaxLength { get; }
    public string Value;
    public override byte[] ToByteArray() => Encoding.UTF8.GetBytes(Value + "\0"); // Adding the null char adds a zero byte to the output.
    public override string ToString()
    {
      return Value;
    }
  }
  /// <summary>
  /// 32-bit integer value.
  /// </summary>
  public class IntegerValue : Value
  {
    public IntegerValue(string name, int value)
      : base(name, SfoEntryType.Integer)
    {
      Type = SfoEntryType.Integer;
      Value = value;
    }
    public override int Length => 4;
    public override int MaxLength => 4;
    public int Value;
    public override byte[] ToByteArray() => BitConverter.GetBytes(Value);
    public override string ToString()
    {
      return $"0x{Value:x8}";
    }
  }
}
