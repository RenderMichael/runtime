﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Xunit;

namespace System.Reflection.Emit.Tests
{
    [ConditionalClass(typeof(PlatformDetection), nameof(PlatformDetection.IsNotBrowser))]
    public class AssemblySaveTypeBuilderTests
    {
        private static readonly AssemblyName s_assemblyName = new AssemblyName("MyDynamicAssembly")
        {
            Version = new Version("1.2.3.4"),
            CultureInfo = Globalization.CultureInfo.CurrentCulture,
            ContentType = AssemblyContentType.WindowsRuntime,
            Flags = AssemblyNameFlags.EnableJITcompileTracking | AssemblyNameFlags.Retargetable,
        };

        [Fact]
        public void EmptyAssemblyAndModuleTest()
        {
            using (TempFile file = TempFile.Create())
            {
                AssemblySaveTools.WriteAssemblyToDisk(s_assemblyName, Type.EmptyTypes, file.Path);
                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Assembly assemblyFromDisk = mlc.LoadFromAssemblyPath(file.Path);
                    Assert.Empty(assemblyFromDisk.GetTypes());
                    AssemblySaveTools.AssertAssemblyNameAndModule(s_assemblyName, assemblyFromDisk.GetName(), assemblyFromDisk.Modules.FirstOrDefault());
                }
            }
        }

        public static IEnumerable<object[]> VariousInterfacesStructsTestData()
        {
            yield return new object[] { new Type[] { typeof(INoMethod) } };
            yield return new object[] { new Type[] { typeof(IMultipleMethod) } };
            yield return new object[] { new Type[] { typeof(INoMethod), typeof(IOneMethod) } };
            yield return new object[] { new Type[] { typeof(IMultipleMethod), typeof(EmptyTestClass) } };
            yield return new object[] { new Type[] { typeof(IMultipleMethod), typeof(EmptyTestClass), typeof(IAccess), typeof(IOneMethod), typeof(INoMethod) } };
            yield return new object[] { new Type[] { typeof(EmptyStruct) } };
            yield return new object[] { new Type[] { typeof(StructWithFields) } };
            yield return new object[] { new Type[] { typeof(StructWithFields), typeof(EmptyStruct) } };
            yield return new object[] { new Type[] { typeof(IMultipleMethod), typeof(StructWithFields), typeof(ClassWithFields), typeof(EmptyTestClass) } };
        }

        [Theory]
        [MemberData(nameof(VariousInterfacesStructsTestData))]
        public void WriteAssemblyWithVariousTypesToAFileAndReadBackTest(Type[] types)
        {
            using (TempFile file = TempFile.Create())
            {
                AssemblySaveTools.WriteAssemblyToDisk(s_assemblyName, types, file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Assembly assemblyFromDisk = mlc.LoadFromAssemblyPath(file.Path);
                    AssertTypesAndTypeMembers(types, assemblyFromDisk.Modules.First().GetTypes());
                }
            }
        }

        private static void AssertTypesAndTypeMembers(Type[] types, Type[] typesFromDisk)
        {
            Assert.Equal(types.Length, typesFromDisk.Length);

            for (int i = 0; i < types.Length; i++)
            {
                Type sourceType = types[i];
                Type typeFromDisk = typesFromDisk[i];

                AssemblySaveTools.AssertTypeProperties(sourceType, typeFromDisk);
                AssemblySaveTools.AssertMethods(sourceType.GetMethods(BindingFlags.DeclaredOnly), typeFromDisk.GetMethods(BindingFlags.DeclaredOnly));
                AssemblySaveTools.AssertFields(sourceType.GetFields(), typeFromDisk.GetFields());
            }
        }

        [Theory]
        [MemberData(nameof(VariousInterfacesStructsTestData))]
        public void WriteAssemblyWithVariousTypesToStreamAndReadBackTest(Type[] types)
        {
            using (var stream = new MemoryStream())
            {
                AssemblySaveTools.WriteAssemblyToStream(s_assemblyName, types, stream);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Assembly assemblyFromStream = mlc.LoadFromStream(stream);
                    AssertTypesAndTypeMembers(types, assemblyFromStream.Modules.First().GetTypes());
                }
            }
        }

        [Fact]
        public void CreateMembersThatUsesTypeLoadedFromCoreAssemblyTest()
        {
            using (TempFile file = TempFile.Create())
            {
                TypeBuilder tb = CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder);
                tb.DefineMethod("TestMethod", MethodAttributes.Public).GetILGenerator().Emit(OpCodes.Ret);
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Assembly assemblyFromDisk = mlc.LoadFromAssemblyPath(file.Path);
                    Module moduleFromDisk = assemblyFromDisk.Modules.First();

                    Assert.Equal("MyModule", moduleFromDisk.ScopeName);
                    Assert.Equal(1, moduleFromDisk.GetTypes().Length);

                    Type testType = moduleFromDisk.GetTypes()[0];
                    Assert.Equal("TestInterface", testType.Name);

                    MethodInfo method = testType.GetMethods()[0];
                    Assert.Equal("TestMethod", method.Name);
                    Assert.Empty(method.GetParameters());
                    Assert.Equal("System.Void", method.ReturnType.FullName);
                }
            }
        }

        private static TypeBuilder CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder)
        {
            assemblyBuilder = AssemblySaveTools.PopulateAssemblyBuilder(s_assemblyName);
            return assemblyBuilder.DefineDynamicModule("MyModule")
                .DefineType("TestInterface", TypeAttributes.Interface | TypeAttributes.Abstract);
        }

        [Fact]
        public void AddInterfaceImplementationTest()
        {
            using (TempFile file = TempFile.Create())
            {
                AssemblyBuilder assemblyBuilder = AssemblySaveTools.PopulateAssemblyBuilder(s_assemblyName);
                ModuleBuilder mb = assemblyBuilder.DefineDynamicModule("My Module");
                TypeBuilder tb = mb.DefineType("TestInterface", TypeAttributes.Interface | TypeAttributes.Abstract, null, [typeof(IOneMethod)]);
                tb.AddInterfaceImplementation(typeof(INoMethod));
                TypeBuilder nestedType = tb.DefineNestedType("NestedType", TypeAttributes.Interface | TypeAttributes.Abstract);
                tb.CreateType();
                nestedType.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Assembly assemblyFromDisk = mlc.LoadFromAssemblyPath(file.Path);
                    Type testType = assemblyFromDisk.Modules.First().GetTypes()[0];
                    Type[] interfaces = testType.GetInterfaces();

                    Assert.Equal("TestInterface", testType.Name);
                    Assert.Equal(2, interfaces.Length);

                    Type iOneMethod = testType.GetInterface("IOneMethod");
                    Type iNoMethod = testType.GetInterface("INoMethod");
                    Type[] nt = testType.GetNestedTypes();
                    Assert.Equal(1, iOneMethod.GetMethods().Length);
                    Assert.Empty(iNoMethod.GetMethods());
                    Assert.NotNull(testType.GetNestedType("NestedType", BindingFlags.NonPublic));
                }
            }
        }

        public static IEnumerable<object[]> TypeParameters()
        {
            yield return new object[] { new string[] { "TFirst", "TSecond", "TThird" } };
            yield return new object[] { new string[] { "TFirst" } };
        }

        [Theory]
        [MemberData(nameof(TypeParameters))]
        public void SaveGenericTypeParametersForAType(string[] typeParamNames)
        {
            using (TempFile file = TempFile.Create())
            {
                TypeBuilder tb = CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder);
                MethodBuilder method = tb.DefineMethod("TestMethod", MethodAttributes.Public);
                method.GetILGenerator().Emit(OpCodes.Ldarg_0);
                GenericTypeParameterBuilder[] typeParams = tb.DefineGenericParameters(typeParamNames);
                if (typeParams.Length > 2)
                {
                    SetVariousGenericParameterValues(typeParams);
                }
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Assembly assemblyFromDisk = mlc.LoadFromAssemblyPath(file.Path);
                    Type testType = assemblyFromDisk.Modules.First().GetTypes()[0];
                    MethodInfo testMethod = testType.GetMethod("TestMethod");
                    Type[] genericTypeParams = testType.GetGenericArguments();

                    Assert.True(testType.IsGenericType);
                    Assert.True(testType.IsGenericTypeDefinition);
                    Assert.True(testType.ContainsGenericParameters);
                    Assert.False(testMethod.IsGenericMethod);
                    Assert.False(testMethod.IsGenericMethodDefinition);
                    Assert.True(testMethod.ContainsGenericParameters);
                    AssertGenericParameters(typeParams, genericTypeParams);
                }
            }
        }

        private static void SetVariousGenericParameterValues(GenericTypeParameterBuilder[] typeParams)
        {
            typeParams[0].SetInterfaceConstraints([typeof(IAccess), typeof(INoMethod)]);
            typeParams[1].SetCustomAttribute(new CustomAttributeBuilder(typeof(DynamicallyAccessedMembersAttribute).GetConstructor(
                [typeof(DynamicallyAccessedMemberTypes)]), [DynamicallyAccessedMemberTypes.PublicProperties]));
            typeParams[2].SetBaseTypeConstraint(typeof(EmptyTestClass));
            typeParams[2].SetGenericParameterAttributes(GenericParameterAttributes.VarianceMask);
        }

        private static void AssertGenericParameters(GenericTypeParameterBuilder[] typeParams, Type[] genericTypeParams)
        {
            Assert.Equal("TFirst", genericTypeParams[0].Name);
            if (typeParams.Length > 2)
            {
                Assert.Equal("TSecond", genericTypeParams[1].Name);
                Assert.Equal("TThird", genericTypeParams[2].Name);
                Type[] constraints = genericTypeParams[0].GetTypeInfo().GetGenericParameterConstraints();
                Assert.Equal(2, constraints.Length);
                Assert.Equal(typeof(IAccess).FullName, constraints[0].FullName);
                Assert.Equal(typeof(INoMethod).FullName, constraints[1].FullName);
                Assert.Empty(genericTypeParams[1].GetTypeInfo().GetGenericParameterConstraints());
                Type[] constraints2 = genericTypeParams[2].GetTypeInfo().GetGenericParameterConstraints();
                Assert.Equal(1, constraints2.Length);
                Assert.Equal(typeof(EmptyTestClass).FullName, constraints2[0].FullName);
                Assert.Equal(GenericParameterAttributes.None, genericTypeParams[0].GenericParameterAttributes);
                Assert.Equal(GenericParameterAttributes.VarianceMask, genericTypeParams[2].GenericParameterAttributes);
                IList<CustomAttributeData> attributes = genericTypeParams[1].GetCustomAttributesData();
                Assert.Equal(1, attributes.Count);
                Assert.Equal("DynamicallyAccessedMembersAttribute", attributes[0].AttributeType.Name);
                Assert.Equal(DynamicallyAccessedMemberTypes.PublicProperties, (DynamicallyAccessedMemberTypes)attributes[0].ConstructorArguments[0].Value);
                Assert.Empty(genericTypeParams[0].GetCustomAttributesData());
            }
        }

        [Theory]
        [MemberData(nameof(TypeParameters))]
        public void SaveGenericTypeParametersForAMethod(string[] typeParamNames)
        {
            using (TempFile file = TempFile.Create())
            {
                TypeBuilder tb = CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder);
                MethodBuilder method = tb.DefineMethod("TestMethod", MethodAttributes.Public);
                GenericTypeParameterBuilder[] typeParams = method.DefineGenericParameters(typeParamNames);
                method.GetILGenerator().Emit(OpCodes.Ldarg_0);
                if (typeParams.Length > 2)
                {
                    SetVariousGenericParameterValues(typeParams);
                }
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Type testType = mlc.LoadFromAssemblyPath(file.Path).Modules.First().GetTypes()[0];
                    MethodInfo testMethod = testType.GetMethod("TestMethod");
                    Type[] genericTypeParams = testMethod.GetGenericArguments();

                    Assert.False(testType.IsGenericType);
                    Assert.False(testType.IsGenericTypeDefinition);
                    Assert.False(testType.ContainsGenericParameters);
                    Assert.True(testMethod.IsGenericMethod);
                    Assert.True(testMethod.IsGenericMethodDefinition);
                    Assert.True(testMethod.ContainsGenericParameters);
                    AssertGenericParameters(typeParams, genericTypeParams);
                }
            }
        }

        [Theory]
        [InlineData(0, "TestInterface[]")]
        [InlineData(1, "TestInterface[]")] // not [*]
        [InlineData(2, "TestInterface[,]")]
        [InlineData(3, "TestInterface[,,]")]
        public void SaveArrayTypeSignature(int rank, string name)
        {
            using (TempFile file = TempFile.Create())
            {
                TypeBuilder tb = CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder);
                Type arrayType = rank == 0 ? tb.MakeArrayType() : tb.MakeArrayType(rank);
                MethodBuilder mb = tb.DefineMethod("TestMethod", MethodAttributes.Public);
                mb.SetReturnType(arrayType);
                mb.SetParameters([typeof(INoMethod), arrayType, typeof(int[,,,])]);
                mb.GetILGenerator().Emit(OpCodes.Ret);
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Type testType = mlc.LoadFromAssemblyPath(file.Path).Modules.First().GetTypes()[0];
                    MethodInfo testMethod = testType.GetMethod("TestMethod");
                    Type intArray = testMethod.GetParameters()[2].ParameterType;

                    Assert.False(testMethod.GetParameters()[0].ParameterType.IsSZArray);
                    Assert.True(intArray.IsArray);
                    Assert.Equal(4, intArray.GetArrayRank());
                    Assert.Equal("Int32[,,,]", intArray.Name);
                    AssertArrayTypeSignature(rank, name, testMethod.ReturnType);
                    AssertArrayTypeSignature(rank, name, testMethod.GetParameters()[1].ParameterType);
                }
            }
        }

        private static void AssertArrayTypeSignature(int rank, string name, Type arrayType)
        {
            Assert.True(rank < 2 ? arrayType.IsSZArray : arrayType.IsArray);
            rank = rank == 0 ? rank + 1 : rank;
            Assert.Equal(rank, arrayType.GetArrayRank());
            Assert.Equal(name, arrayType.Name);
        }

        [Fact]
        public void SaveByRefTypeSignature()
        {
            using (TempFile file = TempFile.Create())
            {
                TypeBuilder tb = CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder);
                Type byrefType = tb.MakeByRefType();
                MethodBuilder mb = tb.DefineMethod("TestMethod", MethodAttributes.Public);
                mb.SetReturnType(byrefType);
                mb.SetParameters([typeof(INoMethod), byrefType]);
                mb.GetILGenerator().Emit(OpCodes.Ret);
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Type testType = mlc.LoadFromAssemblyPath(file.Path).Modules.First().GetTypes()[0];
                    MethodInfo testMethod = testType.GetMethod("TestMethod");

                    Assert.False(testMethod.GetParameters()[0].ParameterType.IsByRef);
                    AssertByRefType(testMethod.GetParameters()[1].ParameterType);
                    AssertByRefType(testMethod.ReturnType);
                }
            }
        }

        private static void AssertByRefType(Type byrefParam)
        {
            Assert.True(byrefParam.IsByRef);
            Assert.Equal("TestInterface&", byrefParam.Name);
        }

        [Fact]
        public void SavePointerTypeSignature()
        {
            using (TempFile file = TempFile.Create())
            {
                TypeBuilder tb = CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder);
                Type pointerType = tb.MakePointerType();
                MethodBuilder mb = tb.DefineMethod("TestMethod", MethodAttributes.Public);
                mb.SetReturnType(pointerType);
                mb.SetParameters([typeof(INoMethod), pointerType]);
                mb.GetILGenerator().Emit(OpCodes.Ret);
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Type testType = mlc.LoadFromAssemblyPath(file.Path).Modules.First().GetTypes()[0];
                    MethodInfo testMethod = testType.GetMethod("TestMethod");

                    Assert.False(testMethod.GetParameters()[0].ParameterType.IsPointer);
                    AssertPointerType(testMethod.GetParameters()[1].ParameterType);
                    AssertPointerType(testMethod.ReturnType);
                }
            }
        }

        private void AssertPointerType(Type testType)
        {
            Assert.True(testType.IsPointer);
            Assert.Equal("TestInterface*", testType.Name);
        }

        [Fact]
        public void GenericTypeParameter_MakePointerType_MakeByRefType_MakeArrayType()
        {
            AssemblySaveTools.PopulateAssemblyBuilderAndTypeBuilder(out TypeBuilder type);
            GenericTypeParameterBuilder[] typeParams = type.DefineGenericParameters("T");
            Type pointerType = typeParams[0].MakePointerType();
            Type byrefType = typeParams[0].MakeByRefType();
            Type arrayType = typeParams[0].MakeArrayType();
            Type arrayType2 = typeParams[0].MakeArrayType(3);
            FieldBuilder field = type.DefineField("Field", pointerType, FieldAttributes.Public);
            FieldBuilder fieldArray = type.DefineField("FieldArray", arrayType2, FieldAttributes.Public);
            MethodBuilder method = type.DefineMethod("TestMethod", MethodAttributes.Public);
            method.SetSignature(arrayType, null, null, [byrefType], null, null);
            method.GetILGenerator().Emit(OpCodes.Ret);
            Type genericIntType = type.MakeGenericType(typeof(int));
            type.CreateType();

            Assert.Equal(pointerType, type.GetField("Field").FieldType);
            Assert.True(type.GetField("Field").FieldType.IsPointer);
            Assert.Equal(arrayType2, type.GetField("FieldArray").FieldType);
            Assert.True(type.GetField("FieldArray").FieldType.IsArray);
            Assert.Equal(3, type.GetField("FieldArray").FieldType.GetArrayRank());
            MethodInfo testMethod = type.GetMethod("TestMethod");
            Assert.Equal(arrayType, testMethod.ReturnType);
            Assert.True(testMethod.ReturnType.IsArray);
            Assert.Equal(1, testMethod.GetParameters().Length);
            Assert.Equal(byrefType, testMethod.GetParameters()[0].ParameterType);
            Assert.True(testMethod.GetParameters()[0].ParameterType.IsByRef);
        }

        public static IEnumerable<object[]> SaveGenericType_TestData()
        {
            yield return new object[] { new string[] { "U", "T" }, new Type[] { typeof(string), typeof(int) }, "TestInterface[System.String,System.Int32]" };
            yield return new object[] { new string[] { "U", "T" }, new Type[] { typeof(MakeGenericTypeClass), typeof(MakeGenericTypeInterface) },
                        "TestInterface[System.Reflection.Emit.Tests.MakeGenericTypeClass,System.Reflection.Emit.Tests.MakeGenericTypeInterface]" };
            yield return new object[] { new string[] { "U" }, new Type[] { typeof(List<string>) }, "TestInterface[System.Collections.Generic.List`1[System.String]]" };
        }

        [Theory]
        [MemberData(nameof(SaveGenericType_TestData))]
        public void SaveGenericTypeSignature(string[] genericParams, Type[] typeArguments, string stringRepresentation)
        {
            using (TempFile file = TempFile.Create())
            {
                TypeBuilder tb = CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder);
                GenericTypeParameterBuilder[] typeGenParam = tb.DefineGenericParameters(genericParams);
                Type genericType = tb.MakeGenericType(typeArguments);
                MethodBuilder mb = tb.DefineMethod("TestMethod", MethodAttributes.Public);
                mb.SetReturnType(genericType);
                mb.SetParameters([typeof(INoMethod), genericType]);
                mb.GetILGenerator().Emit(OpCodes.Ret);
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Type testType = mlc.LoadFromAssemblyPath(file.Path).Modules.First().GetTypes()[0];
                    MethodInfo testMethod = testType.GetMethod("TestMethod");
                    Type paramType = testMethod.GetParameters()[1].ParameterType;

                    Assert.False(testMethod.GetParameters()[0].ParameterType.IsGenericType);
                    AssertGenericType(stringRepresentation, paramType);
                    AssertGenericType(stringRepresentation, testMethod.ReturnType);
                }
            }
        }

        [Fact]
        public void TypeBuilder_GetMethod_ReturnsMethod()
        {
            AssemblySaveTools.PopulateAssemblyBuilderAndTypeBuilder(out TypeBuilder type);
            type.DefineGenericParameters("T");

            MethodBuilder genericMethod = type.DefineMethod("GM", MethodAttributes.Public | MethodAttributes.Static);
            GenericTypeParameterBuilder[] methodParams = genericMethod.DefineGenericParameters("U");
            genericMethod.SetParameters(new[] { methodParams[0] });

            Type genericIntType = type.MakeGenericType(typeof(int));
            MethodInfo createdConstructedTypeMethod = TypeBuilder.GetMethod(genericIntType, genericMethod);
            MethodInfo createdGenericMethod = TypeBuilder.GetMethod(type, genericMethod);

            Assert.True(createdConstructedTypeMethod.IsGenericMethodDefinition);
            Assert.True(createdGenericMethod.IsGenericMethodDefinition);
            Assert.Equal("Type: U", createdConstructedTypeMethod.GetGenericArguments()[0].ToString());
            Assert.Equal("Type: U", createdGenericMethod.GetGenericArguments()[0].ToString());
        }

        [Fact]
        public void TypeBuilder_GetField_DeclaringTypeOfFieldGeneric()
        {
            AssemblySaveTools.PopulateAssemblyBuilderAndTypeBuilder(out TypeBuilder type);
            GenericTypeParameterBuilder[] typeParams = type.DefineGenericParameters("T");

            FieldBuilder field = type.DefineField("Field", typeParams[0].AsType(), FieldAttributes.Public);
            FieldBuilder field2 = type.DefineField("Field2", typeParams[0], FieldAttributes.Public);
            Type genericIntType = type.MakeGenericType(typeof(int));

            Assert.Equal("Field", TypeBuilder.GetField(type.AsType(), field).Name);
            Assert.Equal("Field2", TypeBuilder.GetField(genericIntType, field2).Name);
        }

        [Fact]
        public void GetField_TypeNotGeneric_ThrowsArgumentException()
        {
            AssemblySaveTools.PopulateAssemblyBuilderAndTypeBuilder(out TypeBuilder type);
            FieldBuilder field = type.DefineField("Field", typeof(int), FieldAttributes.Public);

            AssertExtensions.Throws<ArgumentException>("field", () => TypeBuilder.GetField(type, field));
        }

        private static void AssertGenericType(string stringRepresentation, Type paramType)
        {
            Assert.True(paramType.IsGenericType);
            Assert.Equal(stringRepresentation, paramType.ToString());
            Assert.False(paramType.IsGenericParameter);
            Assert.False(paramType.IsGenericTypeDefinition);
            Assert.False(paramType.IsGenericTypeParameter);
            Assert.False(paramType.IsGenericMethodParameter);
        }

        [Fact]
        public void SaveGenericTypeSignatureWithGenericParameter()
        {
            using (TempFile file = TempFile.Create())
            {
                TypeBuilder tb = CreateAssemblyAndDefineType(out AssemblyBuilder assemblyBuilder);
                GenericTypeParameterBuilder[] typeParams = tb.DefineGenericParameters(["U", "T", "P"]);
                MethodBuilder mb = tb.DefineMethod("TestMethod", MethodAttributes.Public);
                GenericTypeParameterBuilder[] methodParams = mb.DefineGenericParameters(["M", "N"]);
                Type genericType = tb.MakeGenericType(typeParams);
                mb.SetReturnType(methodParams[0]);
                mb.SetParameters([typeof(INoMethod), genericType, typeParams[1]]);
                mb.GetILGenerator().Emit(OpCodes.Ret);
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Type testType = mlc.LoadFromAssemblyPath(file.Path).Modules.First().GetTypes()[0];
                    MethodInfo testMethod = testType.GetMethod("TestMethod");
                    Type paramType = testMethod.GetParameters()[1].ParameterType;
                    Type genericParameter = testMethod.GetParameters()[2].ParameterType;

                    Assert.False(testMethod.GetParameters()[0].ParameterType.IsGenericType);
                    AssertGenericType("TestInterface[U,T,P]", paramType);
                    Assert.False(genericParameter.IsGenericType);
                    Assert.True(genericParameter.IsGenericParameter);
                    Assert.False(genericParameter.IsGenericTypeDefinition);
                    Assert.True(genericParameter.IsGenericTypeParameter);
                    Assert.False(genericParameter.IsGenericMethodParameter);
                    Assert.Equal("T", genericParameter.Name);
                    Assert.False(testMethod.ReturnType.IsGenericType);
                    Assert.True(testMethod.ReturnType.IsGenericParameter);
                    Assert.False(testMethod.ReturnType.IsGenericTypeDefinition);
                    Assert.False(testMethod.ReturnType.IsGenericTypeParameter);
                    Assert.True(testMethod.ReturnType.IsGenericMethodParameter);
                    Assert.Equal("M", testMethod.ReturnType.Name);
                }
            }
        }

        [Fact]
        public void SaveMultipleGenericTypeParametersToEnsureSortingWorks()
        {
            using (TempFile file = TempFile.Create())
            {
                AssemblyBuilder assemblyBuilder = AssemblySaveTools.PopulateAssemblyBuilder(s_assemblyName);
                ModuleBuilder mb = assemblyBuilder.DefineDynamicModule("My Module");
                TypeBuilder tb = mb.DefineType("TestInterface1", TypeAttributes.Interface | TypeAttributes.Abstract);
                GenericTypeParameterBuilder[] typeParams = tb.DefineGenericParameters(["U", "T"]);
                typeParams[1].SetInterfaceConstraints([typeof(INoMethod), typeof(IOneMethod)]);
                MethodBuilder m11 = tb.DefineMethod("TwoParameters", MethodAttributes.Public);
                MethodBuilder m12 = tb.DefineMethod("FiveTypeParameters", MethodAttributes.Public);
                MethodBuilder m13 = tb.DefineMethod("OneParameter", MethodAttributes.Public);
                m11.DefineGenericParameters(["M", "N"]);
                m11.GetILGenerator().Emit(OpCodes.Ret);
                GenericTypeParameterBuilder[] methodParams = m12.DefineGenericParameters(["A", "B", "C", "D", "F"]);
                m12.GetILGenerator().Emit(OpCodes.Ret);
                methodParams[2].SetInterfaceConstraints([typeof(IMultipleMethod)]);
                m13.DefineGenericParameters(["T"]);
                m13.GetILGenerator().Emit(OpCodes.Ret);
                TypeBuilder tb2 = mb.DefineType("TestInterface2", TypeAttributes.Interface | TypeAttributes.Abstract);
                tb2.DefineGenericParameters(["TFirst", "TSecond", "TThird"]);
                MethodBuilder m21 = tb2.DefineMethod("TestMethod", MethodAttributes.Public);
                m21.DefineGenericParameters(["X", "Y", "Z"]);
                m21.GetILGenerator().Emit(OpCodes.Ret);
                TypeBuilder tb3 = mb.DefineType("TestType");
                GenericTypeParameterBuilder[] typePar = tb3.DefineGenericParameters(["TOne"]);
                typePar[0].SetBaseTypeConstraint(typeof(EmptyTestClass));
                tb3.CreateType();
                tb2.CreateType();
                tb.CreateType();
                assemblyBuilder.Save(file.Path);

                using (MetadataLoadContext mlc = new MetadataLoadContext(new CoreMetadataAssemblyResolver()))
                {
                    Module m = mlc.LoadFromAssemblyPath(file.Path).Modules.First();
                    Type[] type1Params = m.GetTypes()[0].GetGenericArguments();
                    Type[] type2Params = m.GetTypes()[1].GetGenericArguments();
                    Type[] type3Params = m.GetTypes()[2].GetGenericArguments();

                    Assert.Equal("U", type1Params[0].Name);
                    Assert.Empty(type1Params[0].GetTypeInfo().GetGenericParameterConstraints());
                    Assert.Equal("T", type1Params[1].Name);
                    Assert.Equal(nameof(IOneMethod), type1Params[1].GetTypeInfo().GetGenericParameterConstraints()[1].Name);
                    Assert.Equal("TFirst", type2Params[0].Name);
                    Assert.Equal("TSecond", type2Params[1].Name);
                    Assert.Equal("TThird", type2Params[2].Name);
                    Assert.Equal("TOne", type3Params[0].Name);
                    Assert.Equal(nameof(EmptyTestClass), type3Params[0].GetTypeInfo().GetGenericParameterConstraints()[0].Name);

                    Type[] method11Params = m.GetTypes()[0].GetMethod("TwoParameters").GetGenericArguments();
                    Type[] method12Params = m.GetTypes()[0].GetMethod("FiveTypeParameters").GetGenericArguments();
                    Assert.Equal(nameof(IMultipleMethod), method12Params[2].GetTypeInfo().GetGenericParameterConstraints()[0].Name);
                    Type[] method13Params = m.GetTypes()[0].GetMethod("OneParameter").GetGenericArguments();
                    Type[] method21Params = m.GetTypes()[1].GetMethod("TestMethod").GetGenericArguments();

                    Assert.Equal("M", method11Params[0].Name);
                    Assert.Equal("N", method11Params[1].Name);
                    Assert.Equal("A", method12Params[0].Name);
                    Assert.Equal("F", method12Params[4].Name);
                    Assert.Equal("T", method13Params[0].Name);
                    Assert.Equal("X", method21Params[0].Name);
                    Assert.Equal("Z", method21Params[2].Name);
                }
            }
        }
    }

    // Test Types
    public interface INoMethod
    {
    }

    public interface IMultipleMethod
    {
        string Func(int a, string b);
        IOneMethod MoreFunc();
        StructWithFields DoIExist(int a, string b, bool c);
        void BuildAPerpetualMotionMachine();
    }

    internal interface IAccess
    {
        public Version BuildAI(double field);
        public int DisableRogueAI();
    }

    public interface IOneMethod
    {
        object Func(string a, short b);
    }

    public struct EmptyStruct
    {
    }

    public struct StructWithFields
    {
        public int field1;
        public string field2;
    }

    public class EmptyTestClass
    {
    }

    public class ClassWithFields : EmptyTestClass
    {
        public EmptyTestClass field1;
        public byte field2;
    }
}
