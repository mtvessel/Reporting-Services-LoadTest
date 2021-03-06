﻿// Copyright (c) 2016 Microsoft Corporation. All Rights Reserved.
// Licensed under the MIT License (MIT)

using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.OData.Client;
using Newtonsoft.Json.Linq;
using RSAccessor.PortalAccessor.OData.Model;


namespace RSAccessor.PortalAccessor
{
    public class RSPortalAccessorV1
    {
        internal const string CatalogItemContentType = "application/json;odata.metadata=minimal";

        private readonly string _reportServerPortalUrl;

        public RSPortalAccessorV1(string reportServerPortalUrl)
        {
            _reportServerPortalUrl = reportServerPortalUrl;
        }

        public ICredentials ExecuteCredentials { get; set; }

        public Container CreateContext()
        {
            var container = new Container(new Uri(_reportServerPortalUrl));
            ContextFactory.InitializeContainer(_reportServerPortalUrl, ExecuteCredentials, container);
            return container;
        }

        public static string CreateFullPath(params string[] pathParts)
        {
            var seperator = '/';
            var seperators = new[] { seperator };
            for (int i = 0; i < pathParts.Length; i++)
            {
                pathParts[i] = pathParts[i].TrimStart(seperators).TrimEnd(seperators);
            }

            return (seperator + string.Join(seperator.ToString(), pathParts)).Replace("//", "/");
        }

        public string AddToCatalogItems<T>(
            string itemName,
            string parentFolder,
            byte[] content) where T : CatalogItem, new()
        {
            var item = new T();
            item.Path = CreateFullPath(parentFolder, itemName);
            item.Name = itemName;
            item.Content = content;
            item.Type = ResolveCatalogItemType(item);

            var ctx = CreateContext();
            ctx.AddToCatalogItems(item);
            ProceedIfExists(() => ctx.SaveChanges());

            return item.Path;
        }

        public void AddToCatalogItems(CatalogItem item)
        {
            item.Type = ResolveCatalogItemType(item);
            var ctx = CreateContext();
            ctx.AddToCatalogItems(item);
            ProceedIfExists(() => ctx.SaveChanges());
        }

        public string AddToCatalogItems<T>(
            string itemName,
            string parentFolder,
            string json) where T : CatalogItem, new()
        {
            var path = CreateFullPath(parentFolder, itemName);
            var item = JObject.Parse(json);
            item.Property("Name").Value = itemName;
            item.Property("Path").Value = path;
            var content = item.ToString(Newtonsoft.Json.Formatting.None);

            var credentials = ExecuteCredentials ?? CredentialCache.DefaultNetworkCredentials;
            var context = CreateContext();

            using (var client = new WebClient
            {
                Credentials = credentials,
                BaseAddress = _reportServerPortalUrl,
                Encoding = Encoding.UTF8
            })
            {
                client.Headers.Add(HttpRequestHeader.ContentType, CatalogItemContentType);
                client.Headers.Add(ContextFactory.GetHeaders());
                ProceedIfExists(() => client.UploadString(context.CatalogItems.RequestUri, content));
            }

            return item.Path;
        }

        private static bool IsConflict(Exception ex)
        {
            if (ex.InnerException is DataServiceClientException && (ex.InnerException as DataServiceClientException).StatusCode == 409)
                return true;
            if (ex is WebException && ((ex as WebException).Response as HttpWebResponse).StatusCode == HttpStatusCode.Conflict)
                return true;

            return false;
        }

        private static void ProceedIfExists(Action addAction)
        {
            try
            {
                addAction?.Invoke();
            }
            catch (Exception ex)
            {
                if (!IsConflict(ex))
                    throw;
            }
        }

        private static CatalogItemType ResolveCatalogItemType(CatalogItem catalogItem)
        {
            byte[] fakeContent = { 1 };
            if (catalogItem is Report)
                return CatalogItemType.Report;
            if (catalogItem is MobileReport)
                return CatalogItemType.MobileReport;
            if (catalogItem is Kpi)
                return CatalogItemType.Kpi;
            if (catalogItem is Folder)
            {
                catalogItem.Content = fakeContent;
                return CatalogItemType.Folder;
            }
            if (catalogItem is PowerBIReport)
                return CatalogItemType.PowerBIReport;
            if (catalogItem is DataSource)
            {
                catalogItem.Content = fakeContent;
                return CatalogItemType.DataSource;
            }
            if (catalogItem is PowerBIReport)
                return CatalogItemType.PowerBIReport;

            return CatalogItemType.Unknown;
        }
    }
}