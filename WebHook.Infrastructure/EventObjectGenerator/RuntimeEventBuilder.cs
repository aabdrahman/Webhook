using System.Reflection;
using System.Reflection.Emit;

namespace WebHook.Infrastructure.EventObjectGenerator;

/// <summary>
/// Builds CLR types at runtime from an event catalog's field schema, so event payloads can be
/// deserialised into a typed object and checked for missing or mistyped fields.
/// </summary>
/// <remarks>
/// Types are emitted into a single dynamic assembly that lives for the life of the process.
/// Property names are lower-cased.
/// </remarks>
public static class RuntimeEventBuilder
{
    private static readonly ModuleBuilder _moduleBuilder;

    static RuntimeEventBuilder()
    {
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Assembly.GetExecutingAssembly().GetName().Name), AssemblyBuilderAccess.Run);

        if (assemblyBuilder is null)
        {
            throw new NullReferenceException("Could not get executing assembly.");
        }

        _moduleBuilder = assemblyBuilder.DefineDynamicModule("DynamicEventModule");
    }

    /// <summary>
    /// Gets the runtime type with the given name, creating it if it does not exist yet.
    /// </summary>
    /// <remarks>
    /// An existing type is returned as-is, even if <paramref name="properties"/> has changed since it
    /// was created. The type has no explicit constructor.
    /// </remarks>
    /// <param name="eventTypeName">The name of the type.</param>
    /// <param name="properties">Property names mapped to their types.</param>
    /// <returns>The runtime type.</returns>
    public static Type CreateEventType(string eventTypeName, Dictionary<string, Type> properties)
    {
        //This part is added because the test was throwing exception that the class with name exists. So, we check if a class exists with the same name and return it if it does. This is a temporary fix and should be optimized further.

        var existingType = _moduleBuilder.GetType(eventTypeName);

        if (existingType != null)
        {
            return existingType;
        }

        var typeBuilder = _moduleBuilder.DefineType(eventTypeName, TypeAttributes.Public | TypeAttributes.Class);
        foreach (var property in properties)
        {
            var fieldBuilder = typeBuilder.DefineField($"_{property.Key.ToLower()}", property.Value, FieldAttributes.Private);
            var propertyBuilder = typeBuilder.DefineProperty(property.Key.ToLower(), PropertyAttributes.HasDefault, property.Value, null);
            var getMethodBuilder = typeBuilder.DefineMethod($"get_{property.Key.ToLower()}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, property.Value, Type.EmptyTypes);
            var getIL = getMethodBuilder.GetILGenerator();
            getIL.Emit(OpCodes.Ldarg_0);
            getIL.Emit(OpCodes.Ldfld, fieldBuilder);
            getIL.Emit(OpCodes.Ret);
            var setMethodBuilder = typeBuilder.DefineMethod($"set_{property.Key.ToLower()}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, null, new[] { property.Value });
            var setIL = setMethodBuilder.GetILGenerator();
            setIL.Emit(OpCodes.Ldarg_0);
            setIL.Emit(OpCodes.Ldarg_1);
            setIL.Emit(OpCodes.Stfld, fieldBuilder);
            setIL.Emit(OpCodes.Ret);
            propertyBuilder.SetGetMethod(getMethodBuilder);
            propertyBuilder.SetSetMethod(setMethodBuilder);
        }
        return typeBuilder.CreateType();
    }

    /// <summary>
    /// Creates a new runtime type with a public parameterless constructor and the given properties.
    /// </summary>
    /// <param name="className">The name of the type. Must not already exist in the dynamic module.</param>
    /// <param name="properties">Property names mapped to their types.</param>
    /// <returns>The runtime type.</returns>
    public static Type CreateDynamicClass(string className, Dictionary<string, Type> properties)
    {
        var typeBuilder = _moduleBuilder.DefineType(className, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit);
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

        foreach (var property in properties)
        {
            var fieldBuilder = typeBuilder.DefineField($"_{property.Key.ToLower()}", property.Value, FieldAttributes.Private);
            var propertyBuilder = typeBuilder.DefineProperty(property.Key.ToLower(), PropertyAttributes.HasDefault, property.Value, null);
            var getMethodBuilder = typeBuilder.DefineMethod($"get_{property.Key.ToLower()}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, property.Value, Type.EmptyTypes);
            var getIL = getMethodBuilder.GetILGenerator();
            getIL.Emit(OpCodes.Ldarg_0);
            getIL.Emit(OpCodes.Ldfld, fieldBuilder);
            getIL.Emit(OpCodes.Ret);
            var setMethodBuilder = typeBuilder.DefineMethod($"set_{property.Key.ToLower()}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, null, new[] { property.Value });
            var setIL = setMethodBuilder.GetILGenerator();
            setIL.Emit(OpCodes.Ldarg_0);
            setIL.Emit(OpCodes.Ldarg_1);
            setIL.Emit(OpCodes.Stfld, fieldBuilder);
            setIL.Emit(OpCodes.Ret);
            propertyBuilder.SetGetMethod(getMethodBuilder);
            propertyBuilder.SetSetMethod(setMethodBuilder);
        }
        return typeBuilder.CreateType();
    }

    /// <summary>
    /// Maps catalog field type names (<c>string</c>, <c>int</c>, <c>decimal</c>, <c>datetime</c>,
    /// <c>guid</c>, <c>bool</c>, <c>double</c>, <c>float</c>; case-insensitive) to CLR types.
    /// </summary>
    /// <param name="keyValuePairProperties">Field names mapped to type names.</param>
    /// <returns>Field names mapped to CLR types.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyValuePairProperties"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The dictionary is empty or contains an unsupported type name.</exception>
    public static Dictionary<string, Type> GetPropertyTypes(Dictionary<string, string> keyValuePairProperties)
    {
        if (keyValuePairProperties is null)
        {
            throw new ArgumentNullException(nameof(keyValuePairProperties), "Key-value pair properties cannot be null.");
        }

        if (!keyValuePairProperties.Any())
        {
            throw new ArgumentException("Key-value pair properties cannot be empty.", nameof(keyValuePairProperties));
        }

        Dictionary<string, Type> kvPairsProperties = new Dictionary<string, Type>();

        foreach (var item in keyValuePairProperties)
        {
            Type propType = item.Value.ToLower() switch
            {
                "string" => typeof(string),
                "int" => typeof(int),
                "decimal" => typeof(decimal),
                "datetime" => typeof(DateTime),
                "guid" => typeof(Guid),
                "bool" => typeof(bool),
                "double" => typeof(double),
                "float" => typeof(float),
                _ => throw new ArgumentException($"Unsupported type: {item.Value}")
            };

            kvPairsProperties[item.Key] = propType;
        }

        return kvPairsProperties;
    }

    /// <summary>
    /// Creates an instance of a runtime type and sets its properties.
    /// </summary>
    /// <param name="dynamicType">A type created by this class.</param>
    /// <param name="propertyValues">Property names (case-insensitive) mapped to values.</param>
    /// <returns>The new instance.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A property does not exist or is not writable.</exception>
    public static object CreateDynamicObject(Type dynamicType, Dictionary<string, object> propertyValues)
    {
        if (dynamicType is null)
        {
            throw new ArgumentNullException(nameof(dynamicType), "Dynamic type cannot be null.");
        }
        if (propertyValues is null)
        {
            throw new ArgumentNullException(nameof(propertyValues), "Property values cannot be null.");
        }
        var dynamicObject = Activator.CreateInstance(dynamicType);
        foreach (var property in propertyValues)
        {
            var propInfo = dynamicType.GetProperty(property.Key.ToLower());
            if (propInfo != null && propInfo.CanWrite)
            {
                propInfo.SetValue(dynamicObject, property.Value);
            }
            else
            {
                throw new ArgumentException($"Property '{property.Key}' does not exist or is not writable on type '{dynamicType.Name}'.");
            }
        }
        return dynamicObject;
    }
}
