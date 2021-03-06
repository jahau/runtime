// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace System.Text.Json
{
    public static partial class JsonSerializer
    {
        private static bool HandleDictionary(
            JsonClassInfo elementClassInfo,
            JsonSerializerOptions options,
            Utf8JsonWriter writer,
            ref WriteStack state)
        {
            JsonPropertyInfo jsonPropertyInfo = state.Current.JsonPropertyInfo!;
            if (state.Current.CollectionEnumerator == null)
            {
                IEnumerable? enumerable = (IEnumerable?)jsonPropertyInfo.GetValueAsObject(state.Current.CurrentValue);
                if (enumerable == null)
                {
                    if ((state.Current.JsonClassInfo!.ClassType != ClassType.Object || // Write null dictionary values
                        !jsonPropertyInfo.IgnoreNullValues) && // Ignore ClassType.Object properties if IgnoreNullValues is true
                        state.Current.ExtensionDataStatus != ExtensionDataWriteStatus.Writing) // Ignore null extension property (which is a dictionary)
                    {
                        // Write a null object or enumerable.
                        state.Current.WriteObjectOrArrayStart(ClassType.Dictionary, writer, options, writeNull: true);
                    }

                    if (state.Current.PopStackOnEndCollection)
                    {
                        state.Pop();
                    }

                    return true;
                }

                if (state.Current.ExtensionDataStatus != ExtensionDataWriteStatus.Writing)
                {
                    if (options.ReferenceHandling.ShouldWritePreservedReferences())
                    {
                        if (WriteReference(ref state, writer, options, ClassType.Dictionary, enumerable))
                        {
                            return WriteEndDictionary(ref state);
                        }
                    }
                    else
                    {
                        state.Current.WriteObjectOrArrayStart(ClassType.Dictionary, writer, options);
                    }
                }

                // Let the dictionary return the default IEnumerator from its IEnumerable.GetEnumerator().
                // For IDictionary-derived classes this is normally be IDictionaryEnumerator.
                // For IDictionary<TKey, TVale>-derived classes this is normally IDictionaryEnumerator as well
                // but may be IEnumerable<KeyValuePair<TKey, TValue>> if the dictionary only supports generics.
                state.Current.CollectionEnumerator = enumerable.GetEnumerator();
            }

            if (state.Current.CollectionEnumerator.MoveNext())
            {
                // A dictionary should not have a null KeyValuePair.
                Debug.Assert(state.Current.CollectionEnumerator.Current != null);

                bool obtainedValues = false;
                string? key = default;
                object? value = default;

                // Check for polymorphism.
                if (elementClassInfo.ClassType == ClassType.Unknown)
                {
                    jsonPropertyInfo.GetDictionaryKeyAndValue(ref state.Current, out key, out value);
                    GetRuntimeClassInfo(value, ref elementClassInfo, options);
                    obtainedValues = true;
                }

                if (elementClassInfo.ClassType == ClassType.Value)
                {
                    elementClassInfo.PolicyProperty!.WriteDictionary(ref state, writer);
                }
                else
                {
                    if (!obtainedValues)
                    {
                        jsonPropertyInfo.GetDictionaryKeyAndValue(ref state.Current, out key, out value);
                    }

                    if (options.DictionaryKeyPolicy != null && state.Current.ExtensionDataStatus != ExtensionDataWriteStatus.Writing)
                    {
                        Debug.Assert(key != null);
                        key = options.DictionaryKeyPolicy.ConvertName(key);
                        if (key == null)
                        {
                            ThrowHelper.ThrowInvalidOperationException_SerializerDictionaryKeyNull(options.DictionaryKeyPolicy.GetType());
                        }
                    }

                    // An object or another enumerator requires a new stack frame.
                    state.Push(elementClassInfo, value);

                    state.Current.KeyName = key;
                }

                return false;
            }

            // We are done enumerating.
            if (state.Current.ExtensionDataStatus == ExtensionDataWriteStatus.Writing)
            {
                state.Current.ExtensionDataStatus = ExtensionDataWriteStatus.Finished;
            }
            else
            {
                writer.WriteEndObject();
            }

            return WriteEndDictionary(ref state);
        }

        private static bool WriteEndDictionary(ref WriteStack state)
        {
            if (state.Current.PopStackOnEndCollection)
            {
                state.Pop();
            }
            else
            {
                state.Current.EndDictionary();
            }

            return true;
        }

        internal static void WriteDictionary<TProperty>(
            JsonConverter<TProperty> converter,
            JsonSerializerOptions options,
            ref WriteStackFrame current,
            Utf8JsonWriter writer)
        {
            Debug.Assert(converter != null && current.CollectionEnumerator != null);

            string key;
            TProperty value;
            if (current.CollectionEnumerator is IEnumerator<KeyValuePair<string, TProperty>> enumerator)
            {
                key = enumerator.Current.Key;
                value = enumerator.Current.Value;
            }
            else if (current.CollectionEnumerator is IEnumerator<KeyValuePair<string, object>> polymorphicEnumerator)
            {
                key = polymorphicEnumerator.Current.Key;
                value = (TProperty)polymorphicEnumerator.Current.Value;
            }
            else if (current.CollectionEnumerator is IDictionaryEnumerator iDictionaryEnumerator &&
                iDictionaryEnumerator.Key is string keyAsString)
            {
                key = keyAsString;
                value = (TProperty)iDictionaryEnumerator.Value!;
            }
            else
            {
                throw ThrowHelper.GetNotSupportedException_SerializationNotSupportedCollection(
                    current.JsonPropertyInfo!.DeclaredPropertyType,
                    current.JsonPropertyInfo.ParentClassType,
                    current.JsonPropertyInfo.PropertyInfo);
            }

            Debug.Assert(key != null);

            if (options.DictionaryKeyPolicy != null &&
                // We do not convert extension data.
                current.ExtensionDataStatus != ExtensionDataWriteStatus.Writing)
            {
                key = options.DictionaryKeyPolicy.ConvertName(key);

                if (key == null)
                {
                    ThrowHelper.ThrowInvalidOperationException_SerializerDictionaryKeyNull(options.DictionaryKeyPolicy.GetType());
                }
            }

            if (value == null)
            {
                writer.WriteNull(key);
            }
            else
            {
                writer.WritePropertyName(key);
                converter.Write(writer, value, options);
            }
        }
    }
}
