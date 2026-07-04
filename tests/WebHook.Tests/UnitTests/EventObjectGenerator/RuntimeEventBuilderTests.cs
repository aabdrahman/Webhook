using System.Reflection;
using WebHook.Infrastructure.EventObjectGenerator;
using Xunit;

namespace WebHook.UnitTests.EventObjectGenerator;

/// <summary>
/// Unit tests for <see cref="RuntimeEventBuilder"/>.
///
/// NOTE — BUG IN STATIC CONSTRUCTOR:
/// The original code calls assemblyBuilder.GetDynamicModule("DynamicEventModule")
/// which always returns null because no module has been defined yet.
/// It must be changed to assemblyBuilder.DefineDynamicModule("DynamicEventModule").
/// All tests below are written against the corrected version.
/// </summary>
public sealed class RuntimeEventBuilderTests
{
    // -------------------------------------------------------------------------
    // GetPropertyTypes
    // -------------------------------------------------------------------------

    [Fact]
    public void GetPropertyTypes_AllSupportedTypes_ReturnsCorrectTypeMappings()
    {
        // Arrange
        var input = new Dictionary<string, string>
        {
            { "Name",        "string"   },
            { "Age",         "int"      },
            { "Amount",      "decimal"  },
            { "CreatedDate", "datetime" },
            { "Id",          "guid"     },
            { "IsActive",    "bool"     },
            { "Score",       "double"   },
            { "Rating",      "float"    }
        };

        // Act
        var result = RuntimeEventBuilder.GetPropertyTypes(input);

        // Assert
        Assert.Equal(typeof(string),   result["Name"]);
        Assert.Equal(typeof(int),      result["Age"]);
        Assert.Equal(typeof(decimal),  result["Amount"]);
        Assert.Equal(typeof(DateTime), result["CreatedDate"]);
        Assert.Equal(typeof(Guid),     result["Id"]);
        Assert.Equal(typeof(bool),     result["IsActive"]);
        Assert.Equal(typeof(double),   result["Score"]);
        Assert.Equal(typeof(float),    result["Rating"]);
    }

    [Fact]
    public void GetPropertyTypes_TypeNamesAreCaseInsensitive_ReturnsCorrectTypes()
    {
        // Arrange — mixed casing on type names
        var input = new Dictionary<string, string>
        {
            { "Name",   "STRING"  },
            { "Age",    "INT"     },
            { "Amount", "DECIMAL" }
        };

        // Act
        var result = RuntimeEventBuilder.GetPropertyTypes(input);

        // Assert
        Assert.Equal(typeof(string),  result["Name"]);
        Assert.Equal(typeof(int),     result["Age"]);
        Assert.Equal(typeof(decimal), result["Amount"]);
    }

    [Fact]
    public void GetPropertyTypes_NullInput_ThrowsArgumentNullException()
    {
        // Act
        var ex = Assert.Throws<ArgumentNullException>(
            () => RuntimeEventBuilder.GetPropertyTypes(null!));

        // Assert
        Assert.Equal("keyValuePairProperties", ex.ParamName);
    }

    [Fact]
    public void GetPropertyTypes_EmptyDictionary_ThrowsArgumentException()
    {
        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => RuntimeEventBuilder.GetPropertyTypes(new Dictionary<string, string>()));

        // Assert
        Assert.Equal("keyValuePairProperties", ex.ParamName);
    }

    [Fact]
    public void GetPropertyTypes_UnsupportedType_ThrowsArgumentException()
    {
        // Arrange
        var input = new Dictionary<string, string>
        {
            { "Data", "unsupportedtype" }
        };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => RuntimeEventBuilder.GetPropertyTypes(input));

        Assert.Contains("Unsupported type", ex.Message);
        Assert.Contains("unsupportedtype", ex.Message);
    }

    [Fact]
    public void GetPropertyTypes_SingleProperty_ReturnsSingleEntry()
    {
        // Arrange
        var input = new Dictionary<string, string>
        {
            { "CustomerId", "guid" }
        };

        // Act
        var result = RuntimeEventBuilder.GetPropertyTypes(input);

        // Assert
        Assert.Single(result);
        Assert.Equal(typeof(Guid), result["CustomerId"]);
    }

    // -------------------------------------------------------------------------
    // CreateEventType
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateEventType_ValidProperties_ReturnsNonNullType()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "CustomerId", typeof(string) },
            { "FirstName",  typeof(string) }
        };

        // Act
        var result = RuntimeEventBuilder.CreateEventType("CustomerCreatedEvent", properties);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void CreateEventType_ValidProperties_TypeNameMatchesInput()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "CustomerId", typeof(string) }
        };

        // Act
        var result = RuntimeEventBuilder.CreateEventType("PaymentCompletedEvent", properties);

        // Assert
        Assert.Equal("PaymentCompletedEvent", result.Name);
    }

    [Fact]
    public void CreateEventType_ValidProperties_AllPropertiesExistOnType()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "CustomerId", typeof(string) },
            { "Amount",     typeof(decimal) },
            { "CreatedAt",  typeof(DateTime) }
        };

        // Act
        var result = RuntimeEventBuilder.CreateEventType("OrderCreatedEvent", properties);

        // Assert — properties are stored lowercase per the IL emit logic
        Assert.NotNull(result.GetProperty("customerid"));
        Assert.NotNull(result.GetProperty("amount"));
        Assert.NotNull(result.GetProperty("createdat"));
    }

    [Fact]
    public void CreateEventType_ValidProperties_PropertyTypesAreCorrect()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "CustomerId", typeof(Guid)   },
            { "Amount",     typeof(decimal) }
        };

        // Act
        var result = RuntimeEventBuilder.CreateEventType("InvoiceCreatedEvent", properties);

        // Assert
        Assert.Equal(typeof(Guid),    result.GetProperty("customerid")!.PropertyType);
        Assert.Equal(typeof(decimal), result.GetProperty("amount")!.PropertyType);
    }

    [Fact]
    public void CreateEventType_ValidProperties_PropertiesAreReadWrite()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "Name", typeof(string) }
        };

        // Act
        var result = RuntimeEventBuilder.CreateEventType("AccountApprovedEvent", properties);
        var prop   = result.GetProperty("name");

        // Assert
        Assert.NotNull(prop);
        Assert.True(prop.CanRead);
        Assert.True(prop.CanWrite);
    }

    [Fact]
    public void CreateEventType_IsPublicClass()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "EventId", typeof(Guid) }
        };

        // Act
        var result = RuntimeEventBuilder.CreateEventType("TestPublicEvent", properties);

        // Assert
        Assert.True(result.IsClass);
        Assert.True(result.IsPublic);
    }

    // -------------------------------------------------------------------------
    // CreateDynamicClass
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateDynamicClass_ValidProperties_ReturnsInstantiableType()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "CustomerId", typeof(string) },
            { "Email",      typeof(string) }
        };

        // Act
        var result   = RuntimeEventBuilder.CreateDynamicClass("CustomerPayload", properties);
        var instance = Activator.CreateInstance(result);

        // Assert — CreateDynamicClass defines a default constructor so Activator works
        Assert.NotNull(result);
        Assert.NotNull(instance);
    }

    [Fact]
    public void CreateDynamicClass_ValidProperties_HasDefaultConstructor()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "Name", typeof(string) }
        };

        // Act
        var result = RuntimeEventBuilder.CreateDynamicClass("PayloadWithConstructor", properties);
        var ctor   = result.GetConstructor(Type.EmptyTypes);

        // Assert
        Assert.NotNull(ctor);
    }

    [Fact]
    public void CreateDynamicClass_ValidProperties_AllPropertiesExistOnType()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "FirstName", typeof(string) },
            { "LastName",  typeof(string) },
            { "Age",       typeof(int)    }
        };

        // Act
        var result = RuntimeEventBuilder.CreateDynamicClass("PersonPayload", properties);

        // Assert
        Assert.NotNull(result.GetProperty("firstname"));
        Assert.NotNull(result.GetProperty("lastname"));
        Assert.NotNull(result.GetProperty("age"));
    }

    [Fact]
    public void CreateDynamicClass_ValidProperties_PropertyTypesAreCorrect()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "IsActive", typeof(bool)   },
            { "Score",    typeof(double) }
        };

        // Act
        var result = RuntimeEventBuilder.CreateDynamicClass("ScorePayload", properties);

        // Assert
        Assert.Equal(typeof(bool),   result.GetProperty("isactive")!.PropertyType);
        Assert.Equal(typeof(double), result.GetProperty("score")!.PropertyType);
    }

    // -------------------------------------------------------------------------
    // CreateDynamicObject
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateDynamicObject_ValidTypeAndValues_ReturnsPopulatedObject()
    {
        // Arrange
        var properties = new Dictionary<string, Type>
        {
            { "CustomerId", typeof(string) },
            { "Amount",     typeof(decimal) }
        };
        var dynamicType = RuntimeEventBuilder.CreateDynamicClass("PaymentPayload", properties);

        var values = new Dictionary<string, object>
        {
            { "CustomerId", "cust-001"  },
            { "Amount",     100.50m     }
        };

        // Act
        var result = RuntimeEventBuilder.CreateDynamicObject(dynamicType, values);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("cust-001", result.GetType().GetProperty("customerid")!.GetValue(result));
        Assert.Equal(100.50m,    result.GetType().GetProperty("amount")!.GetValue(result));
    }

    [Fact]
    public void CreateDynamicObject_AllSupportedValueTypes_SetsCorrectly()
    {
        // Arrange
        var id         = Guid.NewGuid();
        var now        = DateTime.UtcNow;

        var properties = new Dictionary<string, Type>
        {
            { "Id",        typeof(Guid)     },
            { "CreatedAt", typeof(DateTime) },
            { "IsActive",  typeof(bool)     },
            { "Score",     typeof(double)   },
            { "Rating",    typeof(float)    },
            { "Count",     typeof(int)      },
            { "Amount",    typeof(decimal)  }
        };

        var dynamicType = RuntimeEventBuilder.CreateDynamicClass("AllTypesPayload", properties);

        var values = new Dictionary<string, object>
        {
            { "Id",        id    },
            { "CreatedAt", now   },
            { "IsActive",  true  },
            { "Score",     9.5d  },
            { "Rating",    4.5f  },
            { "Count",     42    },
            { "Amount",    99.9m }
        };

        // Act
        var result = RuntimeEventBuilder.CreateDynamicObject(dynamicType, values);
        var type   = result.GetType();

        // Assert
        Assert.Equal(id,    type.GetProperty("id")!.GetValue(result));
        Assert.Equal(now,   type.GetProperty("createdat")!.GetValue(result));
        Assert.Equal(true,  type.GetProperty("isactive")!.GetValue(result));
        Assert.Equal(9.5d,  type.GetProperty("score")!.GetValue(result));
        Assert.Equal(4.5f,  type.GetProperty("rating")!.GetValue(result));
        Assert.Equal(42,    type.GetProperty("count")!.GetValue(result));
        Assert.Equal(99.9m, type.GetProperty("amount")!.GetValue(result));
    }

    [Fact]
    public void CreateDynamicObject_NullType_ThrowsArgumentNullException()
    {
        // Act
        var ex = Assert.Throws<ArgumentNullException>(
            () => RuntimeEventBuilder.CreateDynamicObject(null!, new Dictionary<string, object>()));

        // Assert
        Assert.Equal("dynamicType", ex.ParamName);
    }

    [Fact]
    public void CreateDynamicObject_NullValues_ThrowsArgumentNullException()
    {
        // Arrange
        var properties  = new Dictionary<string, Type> { { "Name", typeof(string) } };
        var dynamicType = RuntimeEventBuilder.CreateDynamicClass("NullValuesPayload", properties);

        // Act
        var ex = Assert.Throws<ArgumentNullException>(
            () => RuntimeEventBuilder.CreateDynamicObject(dynamicType, null!));

        // Assert
        Assert.Equal("propertyValues", ex.ParamName);
    }

    [Fact]
    public void CreateDynamicObject_NonExistentProperty_ThrowsArgumentException()
    {
        // Arrange
        var properties  = new Dictionary<string, Type> { { "Name", typeof(string) } };
        var dynamicType = RuntimeEventBuilder.CreateDynamicClass("MissingPropPayload", properties);

        var values = new Dictionary<string, object>
        {
            { "NonExistentProperty", "value" }
        };

        // Act
        var ex = Assert.Throws<ArgumentException>(
            () => RuntimeEventBuilder.CreateDynamicObject(dynamicType, values));

        // Assert
        Assert.Contains("NonExistentProperty", ex.Message);
        Assert.Contains("does not exist", ex.Message);
    }

    // -------------------------------------------------------------------------
    // End-to-end: GetPropertyTypes → CreateDynamicClass → CreateDynamicObject
    // -------------------------------------------------------------------------

    [Fact]
    public void EndToEnd_StringInput_ProducesPopulatedDynamicObject()
    {
        // Arrange — simulate the full pipeline the catalog service would use:
        // admin defines fields as strings → builder resolves types → creates class → populates object

        var rawProperties = new Dictionary<string, string>
        {
            { "CustomerId", "guid"    },
            { "FirstName",  "string"  },
            { "Amount",     "decimal" }
        };

        var id = Guid.NewGuid();

        var propertyValues = new Dictionary<string, object>
        {
            { "CustomerId", id         },
            { "FirstName",  "John"     },
            { "Amount",     250.75m    }
        };

        // Act
        var resolvedTypes = RuntimeEventBuilder.GetPropertyTypes(rawProperties);
        var dynamicType   = RuntimeEventBuilder.CreateDynamicClass("CustomerCreatedPayload", resolvedTypes);
        var dynamicObject = RuntimeEventBuilder.CreateDynamicObject(dynamicType, propertyValues);

        // Assert
        var type = dynamicObject.GetType();
        Assert.Equal(id,       type.GetProperty("customerid")!.GetValue(dynamicObject));
        Assert.Equal("John",   type.GetProperty("firstname")!.GetValue(dynamicObject));
        Assert.Equal(250.75m,  type.GetProperty("amount")!.GetValue(dynamicObject));
    }
}
