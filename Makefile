FILES = src/SimpleJit.X86/Assembler.cs \
		src/SimpleJit.X86/Register.cs \
		src/SimpleJit.X86/IndirectRegister.cs \
		src/SimpleJit.X86/ModRM.cs \
		src/SimpleJit.Extensions/NumericExtensions.cs	\
		src/SimpleJit.Extensions/StreamExtensions.cs	\
		src/SimpleJit.Cil/Opcode.cs \
		src/SimpleJit.Cil/MetadataTableGenerated.cs	\
		src/SimpleJit.Cil/OpcodesTableGenerated.cs \
		src/SimpleJit.Cil/Image.cs	\
		src/SimpleJit.Metadata/MethodData.cs	\
		src/SimpleJit.Compiler/Compiler.cs	\
		src/External/DataConverter.cs

TEST_FILES = test/SimpleJit.X86/RegisterTest.cs \
			 test/SimpleJit.X86/IndirectRegisterTest.cs \
			 test/SimpleJit.X86/AssemblerTest.cs \

COMPILER_FILES = src/SimpleJit.Compiler/Driver.cs	\

SAMPLES = bin/simple-fun.exe

all: bin bin/SimpleJit.dll bin/SimpleJit_test.dll bin/compiler.exe

samples : $(SAMPLES)
bin:
	mkdir bin

bin/SimpleJit.dll: $(FILES)
	mcs -debug /unsafe -target:library -out:bin/SimpleJit.dll $(FILES)

bin/SimpleJit_test.dll: bin/SimpleJit.dll $(TEST_FILES)
	mcs -debug -target:library -out:bin/SimpleJit_test.dll -r:bin/SimpleJit.dll -r:nunit.framework.dll -r:nunit.framework.extensions.dll  -r:nunit.core.dll $(TEST_FILES)  

bin/compiler.exe: bin/SimpleJit.dll $(COMPILER_FILES)
	mcs -debug /unsafe -out:bin/compiler.exe /r:bin/SimpleJit.dll $(COMPILER_FILES)

src/SimpleJit.Cil/OpcodesTableGenerated.cs: src/SimpleJit.Cil.Generators/opcode-emit.rb src/SimpleJit.Cil.Generators/opcodes.xml
	ruby src/SimpleJit.Cil.Generators/opcode-emit.rb  > src/SimpleJit.Cil/OpcodesTableGenerated.cs src/SimpleJit.Cil.Generators/opcodes.xml

src/SimpleJit.Cil/MetadataTableGenerated.cs: src/SimpleJit.Cil.Generators/table-defs-emit.rb
	ruby src/SimpleJit.Cil.Generators/table-defs-emit.rb  > src/SimpleJit.Cil/MetadataTableGenerated.cs

compile: bin/SimpleJit.dll bin/SimpleJit_test.dll
	@echo done

run-test: bin/SimpleJit_test.dll
	 nunit-console2 bin/SimpleJit_test.dll

bin/%.exe: samples/%.cs
	mcs /unsafe /r:bin/SimpleJit.dll -out:$@ $<

.PHONY: run_test