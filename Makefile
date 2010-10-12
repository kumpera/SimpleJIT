FILES = src/SimpleJit.X86/Assembler.cs \
		src/SimpleJit.X86/Register.cs \
		src/SimpleJit.X86/IndirectRegister.cs \
		src/SimpleJit.X86/ModRM.cs \
		src/SimpleJit.Extensions/StreamExtensions.cs	\
		src/SimpleJit.Cil/Opcode.cs \
		src/SimpleJit.Cil/OpcodesTableGenerated.cs \

TEST_FILES = test/SimpleJit.X86/RegisterTest.cs \
			 test/SimpleJit.X86/IndirectRegisterTest.cs \
			 test/SimpleJit.X86/AssemblerTest.cs \

SAMPLES = bin/simple-fun.exe

all: bin bin/SimpleJit.dll bin/SimpleJit_test.dll

samples : $(SAMPLES)
bin:
	mkdir bin

bin/SimpleJit.dll: $(FILES)
	gmcs -debug /unsafe -target:library -out:bin/SimpleJit.dll $(FILES)

bin/SimpleJit_test.dll: bin/SimpleJit.dll $(TEST_FILES)
	gmcs -debug -target:library -out:bin/SimpleJit_test.dll -r:bin/SimpleJit.dll -r:nunit.framework.dll -r:nunit.framework.extensions.dll  -r:nunit.core.dll $(TEST_FILES)  

src/SimpleJit.Cil/OpcodesTableGenerated.cs: src/SimpleJit.Cil.Generators/opcode-emit.rb src/SimpleJit.Cil.Generators/opcodes.xml
	ruby src/SimpleJit.Cil.Generators/opcode-emit.rb  > src/SimpleJit.Cil/OpcodesTableGenerated.cs src/SimpleJit.Cil.Generators/opcodes.xml
	

compile: bin/SimpleJit.dll bin/SimpleJit_test.dll
	@echo done

run-test: bin/SimpleJit_test.dll
	 nunit-console2 bin/SimpleJit_test.dll

bin/%.exe: samples/%.cs
	gmcs /unsafe /r:bin/SimpleJit.dll -out:$@ $<

.PHONY: run_test