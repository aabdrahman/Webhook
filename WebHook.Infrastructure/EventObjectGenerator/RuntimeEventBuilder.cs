using System.Reflection;
using System.Reflection.Emit;

namespace WebHook.Infrastructure.EventObjectGenerator;

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

    public static Dictionary<string, Type> GetPropertyTypes(Dictionary<string, string> keyValuePairProperties)
    {
        if(keyValuePairProperties is null)
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
