//
// Image.cs
//
// Author:
//   Rodrigo Kumpera  <kumpera@gmail.com>
//
//
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using SimpleJit.Extensions;
using SimpleJit.Metadata;
using Mono;

namespace SimpleJit.CIL
{
struct PeSection {
	internal int offset, virtual_size, raw_size, virtual_address;
	internal string name;

	public override string ToString () {
		return string.Format ("['{0}', va {1:X} off {2:X} vsize {3:X} rsize {4:X} ]", name, virtual_address, offset, virtual_size, raw_size);
	}
}

struct StreamHeader {
	internal int offset, size;
	internal string name;

	public override string ToString () {
		return string.Format ("['{0}': offset {1:X} size {2:X}]", name, offset, size);
	}
}

struct PePointer {
	internal int rva, size;
}

struct TableData {
	internal int offset;
	internal int rows;
	internal int size;	
}

/*
TODO Cache sizes and use this information for 2/4 decoding instead of calculating it everytime.
*/

public class Image {
	const int PE_OFFSET = 0x3C;
	const int OPTIONAL_HEADER_SIZE = 224;
	const int CLI_HEADER_SIZE = 72;
	const int MAX_TABLE_SIZE = 1 << 16;
	const int STRING_BIT = 1;
	const int GUID_BIT = 2;
	const int BLOB_BIT = 4;

	internal byte[] data;
	PeSection[] sections;
	StreamHeader[] streams;
	TableData[] tables;
	bool large_string, large_guid, large_blob;

	int entrypoint;

	public Image (byte[] data) {
		this.data = data;
		ReadHeaders ();
		ReadTables ();
	}

	public MethodData LoadMethod (int idx) {
		return new MethodData (this, idx);
	}

	String ReadCString (int offset, int maxSize) {
		int size = 0;
		while (data [offset + size] != 0 && size < maxSize)
			++size;

		char[] c = new char [size];
		for (int i = 0; i < size; ++i)
			c [i] = (char) data [offset + i];
		return new string (c);
	}

	int RvaToOffset (int rva) {
		for (int i = 0; i < sections.Length; ++i) {
			int offset = rva - sections [i].virtual_address;
			if (sections [i].virtual_address <= rva && offset < sections[i].virtual_size) {
				if (offset >= sections [i].raw_size)
					throw new Exception ("rva in zero fill zone");
				return sections [i].offset + offset;
			}
		}
		throw new Exception ("rva not mapped");
	}

	int GetStreamOffset (string stream) {
		for (int i = 0; i < streams.Length; ++i) {
			if (streams [i].name == stream)
				return streams [i].offset;
		}
		throw new Exception ("Could not find stream " + stream);
	}

	PePointer ReadPePointer (int offset) {
		var conv = DataConverter.LittleEndian;
		return new PePointer () { rva = conv.GetInt32 (data, offset), size = conv.GetInt32 (data, offset + 4) };
	}

	void ReadHeaders () {
		var conv = DataConverter.LittleEndian;
		int offset = conv.GetInt32 (data, PE_OFFSET);

		/*PE signature*/
		if (data [offset] != 'P' || data [offset + 1] != 'E')
			throw new Exception ("Invalid PE header");
		offset += 4;

		/*PE file header*/
		var section_count = conv.GetInt16 (data, offset + 2);
		if (conv.GetInt16 (data, offset + 16) != OPTIONAL_HEADER_SIZE)
			throw new Exception ("Invalid optional header size ");
		offset += 20;

		/*PE optional header*/

		/*PE header Standard fields*/
		if (conv.GetInt16 (data, offset) != 0x10B)
			throw new Exception ("Bad PE Magic");

		int cli_header_rva = conv.GetInt32 (data, offset + 208);
		if (conv.GetInt32 (data, offset + 212) != CLI_HEADER_SIZE)
			throw new Exception ("Invalid cli header size");
		offset += 224;

		/*Pe sections*/
		this.sections = new PeSection [section_count];
		for (int i = 0; i < section_count; ++i) {
			sections [i].name = ReadCString (offset, 8);
			sections [i].virtual_size = conv.GetInt32 (data, offset + 8);
			sections [i].virtual_address = conv.GetInt32 (data, offset + 12);
			sections [i].raw_size = conv.GetInt32 (data, offset + 16);
			sections [i].offset = conv.GetInt32 (data, offset + 20);
			offset += 40;

			Console.WriteLine ("section[{0}] {1}", i, sections[i].ToString ());
		}

		/*cli header*/
		offset = RvaToOffset (cli_header_rva);
		var metadata = ReadPePointer (offset + 8);
		entrypoint = conv.GetInt32 (data, offset + 20);
		//TODO runtime flags, strong names and VTF

		/*metadata root*/
		var metadata_offset = RvaToOffset ((int)metadata.rva);
		offset = (int) metadata_offset;

		if (conv.GetInt32 (data, offset) != 0x424A5342)
			throw new Exception ("Invalid metadata root");
		var version_len = conv.GetInt32 (data, offset + 12).RoundUp (4);
		var version = ReadCString (offset + 16, version_len);
		Console.WriteLine ("runtime version {0}", version);

		/*metadata root - flags */
		offset += 16 + version_len;
		var stream_count = conv.GetInt16 (data, offset + 2);
		offset += 4;

		/*Metadata streams*/
		this.streams = new StreamHeader [stream_count];
		for (int i = 0; i < stream_count; ++i) {
			streams [i].offset = metadata_offset + conv.GetInt32 (data, offset);
			streams [i].size = conv.GetInt32 (data, offset + 4);
			streams [i].name = ReadCString (offset + 8, 32);
			offset += 8 + (streams [i].name + 1).Length.RoundUp (4);
			Console.WriteLine ("stream {0}", streams [i].ToString ());
		}
	}

	internal static int HeapSize (byte heap_sizes, int bit, int count) {
		return count * (((heap_sizes & bit) == bit) ? 4 : 2);
	}

	internal int CodedIndexSize (int max_table_size, Table[] encoded_tables) {
		foreach (Table t in encoded_tables)
			if (t != Table.NotUsed && tables [(int)t].rows >= max_table_size)
				return 4;
		return 2;
	}

	internal int TableIndexSize (Table table) {
		if (tables [(int)table].rows >= MAX_TABLE_SIZE)
			return 4;
		return 2;
	}

	internal int RowOffset (Table table, int row) {
		return tables [(int)table].offset + tables [(int)table].size * row;
	}

	internal uint ReadIndex (bool large, int offset) {
		return large ? DataConverter.UInt32FromLE (data, offset) : DataConverter.UInt16FromLE (data, offset);		
	}

	internal uint ReadTableIndex (Table table, int offset) {
		return ReadIndex (TableIndexSize (table) == 4, offset);
	}

	internal uint ReadStringIndex (int offset) {
		return ReadIndex (large_string, offset);
	}

	internal uint ReadGuidIndex (int offset) {
		return ReadIndex (large_guid, offset);
	}

	internal uint ReadBlobIndex (int offset) {
		return ReadIndex (large_blob, offset);
	}

	internal int StringIndexSize {
		 get { return large_string ? 4 : 2; }
	}

	internal int GuidIndexSize {
		 get { return large_guid ? 4 : 2; }
	}
	
	internal int BlobIndexSize {
		 get { return large_blob ? 4 : 2; }
	}

	void ReadTables () {
		var conv = DataConverter.LittleEndian;
		int offset = GetStreamOffset ("#~");

		var heap_sizes = data [offset + 6];
		large_string = (heap_sizes & STRING_BIT) != 0;
		large_guid = (heap_sizes & GUID_BIT) != 0;
		large_blob = (heap_sizes & BLOB_BIT) != 0;

		var valid_tables = conv.GetInt64 (data, offset + 8);
		tables = new TableData [(int)Table.MaxTableId + 1];
		offset += 24;
		for (int i = 0; i <= (int)Table.MaxTableId; ++i) {
			if ((valid_tables & (1L << i)) != 0) {
				tables [i].rows = conv.GetInt32 (data, offset);
				offset += 4;
			}
		}

		for (int i = 0; i <= (int)Table.MaxTableId; ++i) {
			if (tables [i].rows > 0) {
				tables [i].offset = offset;
				tables [i].size = TableDecoder.DecodeRowSize (this, i);
				offset += tables [i].rows * tables [i].size;
				Console.WriteLine ("table {0} has {1} rows size {2} offset 0x{3:x}", 
					(Table)i, tables [i].rows,  tables [i].size, tables [i].offset);
			}
		}
	}
}

}