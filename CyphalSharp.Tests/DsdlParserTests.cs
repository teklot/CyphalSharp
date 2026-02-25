namespace CyphalSharp.Tests;

public class DsdlParserTests
{
    private readonly string _dsdlPath;

    public DsdlParserTests()
    {
        // Assuming the DSDL folder is in the project root relative to the test execution
        _dsdlPath = Path.Combine(Directory.GetCurrentDirectory(), "DSDL");
        
        // If the above doesn't work in the test environment, we might need to adjust it
        if (!Directory.Exists(_dsdlPath))
        {
             _dsdlPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "CyphalSharp", "DSDL"));
        }
    }

    [Fact]
    public void ParseDirectory_LoadsAllStandardFiles()
    {
        var dsdls = DsdlParser.ParseDirectory(_dsdlPath);
        
        Assert.NotEmpty(dsdls);
        Assert.Contains(dsdls.Keys, k => k.Contains("Heartbeat.1.0.dsdl"));
        Assert.Contains(dsdls.Keys, k => k.Contains("GetInfo.1.0.dsdl"));
    }

    [Fact]
    public void ParseFile_Heartbeat_HasCorrectFields()
    {
        var file = Path.Combine(_dsdlPath, "uavcan", "node", "Heartbeat.1.0.dsdl");
        var dsdl = DsdlParser.ParseFile(file, "uavcan.node.Heartbeat", 1, 0);
        var msg = dsdl.Messages.First();

        Assert.Equal(4, msg.Fields.Count);
        Assert.Equal("uptime", msg.Fields[0].Name);
        Assert.Equal("uint32_t", msg.Fields[0].Type);
        Assert.Equal(32, msg.Fields[0].BitLength);
        
        Assert.Equal("health", msg.Fields[1].Name);
        Assert.Equal(8, msg.Fields[1].BitLength);

        Assert.Equal(7, msg.PayloadLength); // 4 + 1 + 1 + 1
    }

    [Fact]
    public void ParseFile_GetInfo_IsService()
    {
        var file = Path.Combine(_dsdlPath, "uavcan", "node", "GetInfo.1.0.dsdl");
        var dsdl = DsdlParser.ParseFile(file, "uavcan.node.GetInfo", 1, 0);
        var msg = dsdl.Messages.First();

        Assert.True(msg.IsServiceDefinition);
        Assert.Empty(msg.Fields); // GetInfo request is empty
        Assert.NotEmpty(msg.ResponseFields);
        
        // Response has protocol_version, hardware_version, software_version, unique_id, name, software_vcs_revision_id
        Assert.Equal(6, msg.ResponseFields.Count);
        
        var nameField = msg.ResponseFields.First(f => f.Name == "name");
        Assert.Contains("uint8_t", nameField.Type);
        Assert.True(nameField.IsArray);
    }

    [Fact]
    public void ParseFile_Severity_IgnoresConstants()
    {
        var file = Path.Combine(_dsdlPath, "uavcan", "diagnostic", "Severity.1.0.dsdl");
        var dsdl = DsdlParser.ParseFile(file, "uavcan.diagnostic.Severity", 1, 0);
        var msg = dsdl.Messages.First();

        // Should only have 'value' field, other 9 are constants
        Assert.Single(msg.Fields);
        Assert.Equal("value", msg.Fields[0].Name);
    }

    [Fact]
    public void ParseFile_RegisterValue_IsUnion()
    {
        var file = Path.Combine(_dsdlPath, "uavcan", "register", "Value.1.0.dsdl");
        var dsdl = DsdlParser.ParseFile(file, "uavcan.register.Value", 1, 0);
        var msg = dsdl.Messages.First();

        Assert.True(msg.IsUnion);
        Assert.True(msg.UnionTagFieldIndex >= 0);
    }

    [Fact]
    public void ParseFile_WithPortIdDirective_ExtractsPortId()
    {
        var tempDsdl = Path.Combine(Path.GetTempPath(), $"TestPortId.{DateTime.Now.Ticks}.dsdl");
        try
        {
            File.WriteAllText(tempDsdl, "@12345\nuint32 value");
            
            var dsdl = DsdlParser.ParseFile(tempDsdl, "test.TestPortId", 1, 0);
            var msg = dsdl.Messages.First();

            Assert.Equal(12345u, msg.PortId);
        }
        finally
        {
            if (File.Exists(tempDsdl)) File.Delete(tempDsdl);
        }
    }

    [Fact]
    public void ParseFile_WithKeyDirective_ExtractsPortId()
    {
        var tempDsdl = Path.Combine(Path.GetTempPath(), $"TestKey.{DateTime.Now.Ticks}.dsdl");
        try
        {
            File.WriteAllText(tempDsdl, "@__key__ 54321\nuint16 data");
            
            var dsdl = DsdlParser.ParseFile(tempDsdl, "test.TestKey", 1, 0);
            var msg = dsdl.Messages.First();

            Assert.Equal(54321u, msg.PortId);
        }
        finally
        {
            if (File.Exists(tempDsdl)) File.Delete(tempDsdl);
        }
    }

    [Fact]
    public void ParseFile_WithPortIdOverride_UsesOverride()
    {
        var tempDsdl = Path.Combine(Path.GetTempPath(), $"TestOverride.{DateTime.Now.Ticks}.dsdl");
        try
        {
            File.WriteAllText(tempDsdl, "@100\nuint8 field");
            
            var dsdl = DsdlParser.ParseFile(tempDsdl, "test.TestOverride", 1, 0, 99999);
            var msg = dsdl.Messages.First();

            Assert.Equal(99999u, msg.PortId);
        }
        finally
        {
            if (File.Exists(tempDsdl)) File.Delete(tempDsdl);
        }
    }

    [Fact]
    public void GetActiveUnionField_ReturnsCorrectField()
    {
        var tempDsdl = Path.Combine(Path.GetTempPath(), $"TestUnion.{DateTime.Now.Ticks}.dsdl");
        try
        {
            File.WriteAllText(tempDsdl, "@union\nuint8 tag\nuint32 field_a\nuint16 field_b");
            
            var dsdl = DsdlParser.ParseFile(tempDsdl, "test.TestUnion", 1, 0);
            var msg = dsdl.Messages.First();

            Assert.True(msg.IsUnion);
            Assert.Equal(3, msg.Fields.Count); // tag + field_a + field_b

            var activeField = msg.GetActiveUnionField(0);
            Assert.NotNull(activeField);
            Assert.Equal("field_a", activeField.Name);

            activeField = msg.GetActiveUnionField(1);
            Assert.NotNull(activeField);
            Assert.Equal("field_b", activeField.Name);

            activeField = msg.GetActiveUnionField(99); // out of range
            Assert.Null(activeField);
        }
        finally
        {
            if (File.Exists(tempDsdl)) File.Delete(tempDsdl);
        }
    }
}
