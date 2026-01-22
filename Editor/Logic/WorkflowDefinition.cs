using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

// Using an enum provides type safety for our parameter types.
// The StringEnumConverter will ensure it correctly reads "string", "number", etc. from the JSON.
[JsonConverter(typeof(StringEnumConverter))]
public enum ParamType
{
    boolean,
    number,
    @string,
    image,
    mesh
}

// A plain C# class to hold the data for a single input parameter.
public class InputParamDef
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("type")]
    public ParamType Type;

    [JsonProperty("label")]
    public string Label;

    [JsonProperty("default_value")]
    public object DefaultValue;
}

// A plain C# class for an output parameter.
public class OutputParamDef
{
    [JsonProperty("id")]
    public string Id;

    [JsonProperty("type")]
    public ParamType Type;

    [JsonProperty("format")]
    public string Format;
}

// This is the main class representing the entire workflow file.
public class WorkflowDefinition
{
    [JsonProperty("version")]
    public string Version;

    [JsonProperty("api_id")]
    public string ApiId;

    [JsonProperty("base_url")]
    public string BaseUrl;

    [JsonProperty("name")]
    public string Name;

    [JsonProperty("inputs")]
    public List<InputParamDef> Inputs = new List<InputParamDef>();

    [JsonProperty("outputs")]
    public List<OutputParamDef> Outputs = new List<OutputParamDef>();

    // A helper function to easily create an instance from a JSON string.
    public static WorkflowDefinition FromJson(string jsonString)
    {
        return JsonConvert.DeserializeObject<WorkflowDefinition>(jsonString);
    }
}