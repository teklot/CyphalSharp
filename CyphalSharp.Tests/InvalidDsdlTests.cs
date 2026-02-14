namespace CyphalSharp.Tests;

public class InvalidDsdlTests
{
    [Fact]
    public void Parse_InvalidFilename_IsSkipped()
    {
        // Create a temporary invalid file
        string path = Path.Combine(Directory.GetCurrentDirectory(), "InvalidFile.dsdl");
        File.WriteAllText(path, "uint8 value");
        
        try
        {
            var dsdls = DsdlParser.ParseDirectory(Directory.GetCurrentDirectory());
            // Should not contain the file because it doesn't match Type.Major.Minor.dsdl
            Assert.DoesNotContain(dsdls.Keys, k => k == "InvalidFile.dsdl");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Parse_UnknownType_FallsBackGracefully()
    {
        // A valid filename but invalid content
        string path = Path.Combine(Directory.GetCurrentDirectory(), "UnknownType.1.0.dsdl");
        File.WriteAllText(path, "SuperCustomType value");

        try
        {
            var dsdl = DsdlParser.ParseFile(path, "uavcan.test.UnknownType", 1, 0);
            var msg = dsdl.Messages.First();
            
            Assert.Single(msg.Fields);
            Assert.Equal("SuperCustomType", msg.Fields[0].Type);
            // Field.SetDataType falls back to byte for unknown types
            msg.Fields[0].SetDataType();
            Assert.Equal(typeof(byte), msg.Fields[0].DataType);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
