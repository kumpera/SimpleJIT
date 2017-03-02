using SimpleJit.Testing;

public static class Foo {
	public static int test_0_return_const ()
	{
		return 0;
	}

	public static int test_7_add_vals ()
	{
		int a = 4;
		int b = 3;
		int c = a + b;
		return c;
	}

	public static int test_9_add_const ()
	{
		int a = 4;
		int c = a + 5;
		return c;
	}

	public static int test_4_1_3_add_arguments (int a, int b)
	{
		return a + b;
	}
}
