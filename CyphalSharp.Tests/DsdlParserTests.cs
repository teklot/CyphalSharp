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
}
