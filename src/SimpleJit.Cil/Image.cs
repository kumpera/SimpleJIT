using System;
using SimpleJit.Extensions;
using Mono;

namespace BasicJit.CIL
{

struct PeSection {
	internal int offset, virtual_size, raw_size, virtual_address;
	internal string name;

	public override string ToString () {
		return string.Format ("['{0}', va {1:X} off {2:X} vsize {3:X} rsize {4:X} ]", name, virtual_address, offset, virtual_size, raw_size);
	}
}

struct PePointer {
	internal int rva, size;
}

public class Image {
	const int PE_OFFSET = 0x3C;
	const int OPTIONAL_HEADER_SIZE = 224;
	const int CLI_HEADER_SIZE = 72;

	byte[] data;
	PeSection[] sections;
	int entrypoint;

	public Image (byte[] data) {
		this.data = data;
		ReadData ();
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

	PePointer ReadPePointer (int offset) {
		Console.WriteLine ("offset {0}", offset);
		var conv = DataConverter.LittleEndian;
		return new PePointer () { rva = conv.GetInt32 (data, offset), size = conv.GetInt32 (data, offset + 4) };
	}

	void ReadData () {
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
		offset = RvaToOffset (metadata.rva);		
	}


}

}