//
// RegisterTest.cs
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

using System.IO;

using NUnit.Framework;
using NUnit.Framework.Extensions;


namespace SimpleJit {

[TestFixture]
public class RegisterTest {

	void TestEncode (Register a, Register b, byte expected) {
		MemoryStream ms = new MemoryStream ();
		a.EncodeModRm (ms, b);
		Assert.AreEqual (1, ms.Length, string.Format ("stream length {0},{1}", a, b));
		ms.Position = 0;
		Assert.AreEqual (expected, (byte)ms.ReadByte (), string.Format ("encoded ModRM {0},{1}", a, b));
	}

	[Test]
	public void RegRegModRmEncoding ()
	{
		TestEncode (Register.EAX, Register.EAX, 0xC0);
		TestEncode (Register.ECX, Register.EAX, 0xC1);
		TestEncode (Register.EDX, Register.EAX, 0xC2);
		TestEncode (Register.EBX, Register.EAX, 0xC3);
		TestEncode (Register.ESP, Register.EAX, 0xC4);
		TestEncode (Register.EBP, Register.EAX, 0xC5);
		TestEncode (Register.ESI, Register.EAX, 0xC6);
		TestEncode (Register.EDI, Register.EAX, 0xC7);

		TestEncode (Register.EAX, Register.ECX, 0xC8);
		TestEncode (Register.ECX, Register.ECX, 0xC9);
		TestEncode (Register.EDX, Register.ECX, 0xCA);
		TestEncode (Register.EBX, Register.ECX, 0xCB);
		TestEncode (Register.ESP, Register.ECX, 0xCC);
		TestEncode (Register.EBP, Register.ECX, 0xCD);
		TestEncode (Register.ESI, Register.ECX, 0xCE);
		TestEncode (Register.EDI, Register.ECX, 0xCF);


		TestEncode (Register.EAX, Register.EDX, 0xD0);
		TestEncode (Register.ECX, Register.EDX, 0xD1);
		TestEncode (Register.EDX, Register.EDX, 0xD2);
		TestEncode (Register.EBX, Register.EDX, 0xD3);
		TestEncode (Register.ESP, Register.EDX, 0xD4);
		TestEncode (Register.EBP, Register.EDX, 0xD5);
		TestEncode (Register.ESI, Register.EDX, 0xD6);
		TestEncode (Register.EDI, Register.EDX, 0xD7);

		TestEncode (Register.EAX, Register.EBX, 0xD8);
		TestEncode (Register.ECX, Register.EBX, 0xD9);
		TestEncode (Register.EDX, Register.EBX, 0xDA);
		TestEncode (Register.EBX, Register.EBX, 0xDB);
		TestEncode (Register.ESP, Register.EBX, 0xDC);
		TestEncode (Register.EBP, Register.EBX, 0xDD);
		TestEncode (Register.ESI, Register.EBX, 0xDE);
		TestEncode (Register.EDI, Register.EBX, 0xDF);


		TestEncode (Register.EAX, Register.ESP, 0xE0);
		TestEncode (Register.ECX, Register.ESP, 0xE1);
		TestEncode (Register.EDX, Register.ESP, 0xE2);
		TestEncode (Register.EBX, Register.ESP, 0xE3);
		TestEncode (Register.ESP, Register.ESP, 0xE4);
		TestEncode (Register.EBP, Register.ESP, 0xE5);
		TestEncode (Register.ESI, Register.ESP, 0xE6);
		TestEncode (Register.EDI, Register.ESP, 0xE7);

		TestEncode (Register.EAX, Register.EBP, 0xE8);
		TestEncode (Register.ECX, Register.EBP, 0xE9);
		TestEncode (Register.EDX, Register.EBP, 0xEA);
		TestEncode (Register.EBX, Register.EBP, 0xEB);
		TestEncode (Register.ESP, Register.EBP, 0xEC);
		TestEncode (Register.EBP, Register.EBP, 0xED);
		TestEncode (Register.ESI, Register.EBP, 0xEE);
		TestEncode (Register.EDI, Register.EBP, 0xEF);


		TestEncode (Register.EAX, Register.ESI, 0xF0);
		TestEncode (Register.ECX, Register.ESI, 0xF1);
		TestEncode (Register.EDX, Register.ESI, 0xF2);
		TestEncode (Register.EBX, Register.ESI, 0xF3);
		TestEncode (Register.ESP, Register.ESI, 0xF4);
		TestEncode (Register.EBP, Register.ESI, 0xF5);
		TestEncode (Register.ESI, Register.ESI, 0xF6);
		TestEncode (Register.EDI, Register.ESI, 0xF7);

		TestEncode (Register.EAX, Register.EDI, 0xF8);
		TestEncode (Register.ECX, Register.EDI, 0xF9);
		TestEncode (Register.EDX, Register.EDI, 0xFA);
		TestEncode (Register.EBX, Register.EDI, 0xFB);
		TestEncode (Register.ESP, Register.EDI, 0xFC);
		TestEncode (Register.EBP, Register.EDI, 0xFD);
		TestEncode (Register.ESI, Register.EDI, 0xFE);
		TestEncode (Register.EDI, Register.EDI, 0xFF);
	}
}

}