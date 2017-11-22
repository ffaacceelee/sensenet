﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.Search.Indexing;
using SenseNet.Tools;

namespace SenseNet.ContentRepository.Search.Indexing
{
    //UNDONE: XMLDOC CustomIndexField
    public interface IHasCustomIndexField { }
    //UNDONE: XMLDOC CustomIndexField
    public interface ICustomIndexFieldProvider
    {
        //UNDONE: XMLDOC CustomIndexField
        IEnumerable<IndexField> GetFields(IndexDocumentData docData);
    }
    internal class CustomIndexFieldManager
    {
        internal static IEnumerable<IndexField> GetFields(IndexDocument indexDocument, IndexDocumentData indexDocumentData)
        {
            Debug.WriteLine("%> adding custom fields for " + indexDocumentData.Path);
            return Instance.GetFieldsPrivate(indexDocumentData);
        }

        // -------------------------------------------------------------

        private static readonly object InstanceSync = new object();
        private static CustomIndexFieldManager _instance;
        private static CustomIndexFieldManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceSync)
                    {
                        if (_instance == null)
                        {
                            _instance = new CustomIndexFieldManager
                            {
                                _providers = TypeResolver.GetTypesByInterface(typeof(ICustomIndexFieldProvider))
                                    .Select(t => (ICustomIndexFieldProvider) Activator.CreateInstance(t)).ToArray()
                            };
                        }
                    }
                }
                return _instance;
            }
        }

        // ---------------------------------------------------------------------

        private IEnumerable<ICustomIndexFieldProvider> _providers;

        private CustomIndexFieldManager() { }

        private IEnumerable<IndexField> GetFieldsPrivate(IndexDocumentData indexDocumentData)
        {
            var fields = new List<IndexField>();
            foreach (var provider in _providers)
            {
                var f = provider.GetFields(indexDocumentData);
                if (f != null)
                    fields.AddRange(f);
            }
            return fields.Count == 0 ? null : fields;
        }
    }
}
