Console.WriteLine("Hello, World!".DoIt());

internal class Foo
{
    public static int DoItExtension(string foo)
    {
        return foo.Length;
    }
}