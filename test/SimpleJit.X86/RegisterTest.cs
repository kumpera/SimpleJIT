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


namespace SimpleJit.X86 {

[TestFixture]
public class RegisterTest : BuiltinRegisters {

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
		TestEncode (EAX, EAX, 0xC0);
		TestEncode (ECX, EAX, 0xC1);
		TestEncode (EDX, EAX, 0xC2);
		TestEncode (EBX, EAX, 0xC3);
		TestEncode (ESP, EAX, 0xC4);
		TestEncode (EBP, EAX, 0xC5);
		TestEncode (ESI, EAX, 0xC6);
		TestEncode (EDI, EAX, 0xC7);

		TestEncode (EAX, ECX, 0xC8);
		TestEncode (ECX, ECX, 0xC9);
		TestEncode (EDX, ECX, 0xCA);
		TestEncode (EBX, ECX, 0xCB);
		TestEncode (ESP, ECX, 0xCC);
		TestEncode (EBP, ECX, 0xCD);
		TestEncode (ESI, ECX, 0xCE);
		TestEncode (EDI, ECX, 0xCF);


		TestEncode (EAX, EDX, 0xD0);
		TestEncode (ECX, EDX, 0xD1);
		TestEncode (EDX, EDX, 0xD2);
		TestEncode (EBX, EDX, 0xD3);
		TestEncode (ESP, EDX, 0xD4);
		TestEncode (EBP, EDX, 0xD5);
		TestEncode (ESI, EDX, 0xD6);
		TestEncode (EDI, EDX, 0xD7);

		TestEncode (EAX, EBX, 0xD8);
		TestEncode (ECX, EBX, 0xD9);
		TestEncode (EDX, EBX, 0xDA);
		TestEncode (EBX, EBX, 0xDB);
		TestEncode (ESP, EBX, 0xDC);
		TestEncode (EBP, EBX, 0xDD);
		TestEncode (ESI, EBX, 0xDE);
		TestEncode (EDI, EBX, 0xDF);


		TestEncode (EAX, ESP, 0xE0);
		TestEncode (ECX, ESP, 0xE1);
		TestEncode (EDX, ESP, 0xE2);
		TestEncode (EBX, ESP, 0xE3);
		TestEncode (ESP, ESP, 0xE4);
		TestEncode (EBP, ESP, 0xE5);
		TestEncode (ESI, ESP, 0xE6);
		TestEncode (EDI, ESP, 0xE7);

		TestEncode (EAX, EBP, 0xE8);
		TestEncode (ECX, EBP, 0xE9);
		TestEncode (EDX, EBP, 0xEA);
		TestEncode (EBX, EBP, 0xEB);
		TestEncode (ESP, EBP, 0xEC);
		TestEncode (EBP, EBP, 0xED);
		TestEncode (ESI, EBP, 0xEE);
		TestEncode (EDI, EBP, 0xEF);


		TestEncode (EAX, ESI, 0xF0);
		TestEncode (ECX, ESI, 0xF1);
		TestEncode (EDX, ESI, 0xF2);
		TestEncode (EBX, ESI, 0xF3);
		TestEncode (ESP, ESI, 0xF4);
		TestEncode (EBP, ESI, 0xF5);
		TestEncode (ESI, ESI, 0xF6);
		TestEncode (EDI, ESI, 0xF7);

		TestEncode (EAX, EDI, 0xF8);
		TestEncode (ECX, EDI, 0xF9);
		TestEncode (EDX, EDI, 0xFA);
		TestEncode (EBX, EDI, 0xFB);
		TestEncode (ESP, EDI, 0xFC);
		TestEncode (EBP, EDI, 0xFD);
		TestEncode (ESI, EDI, 0xFE);
		TestEncode (EDI, EDI, 0xFF);
	}
}

}