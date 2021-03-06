﻿using Hl7.Fhir.Utility;
using Hl7.Fhir.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Hl7.Fhir.Rest;

namespace Spark.Engine.Extensions
{
    public static class SearchParameterExtensions
    {
        private const string xpathSeparator = "/";
        private const string pathSeparator = ".";
        private const string generalPathPattern = @"(?<chainPart>(?<element>[^{0}\(]+)(?<predicate>\((?<propname>[^=]*)=(?<filterValue>[^\)]*)\))?((?<separator>{0})|(?<endofinput>$)))+";
        public static Regex xpathPattern = new Regex(String.Format(@"(?<root>^//)" + generalPathPattern, xpathSeparator));
        public static Regex pathPattern = new Regex(String.Format(generalPathPattern, @"\" + pathSeparator));

        public static void SetPropertyPath(this SearchParameter searchParameter, string[] paths)
        {
            string[] workingPaths;
            if (paths != null)
            {
                // TODO: Added FirstOrDefault to searchParameter.Base.GetLiteral() could possibly generate a bug

                //A searchparameter always has a Resource as focus, so we don't need the name of the resource to be at the start of the Path.
                //See also: https://github.com/ewoutkramer/fhirpath/blob/master/fhirpath.md
                workingPaths = paths.Select<string, string>(pp => StripResourceNameFromStart(pp, searchParameter.Base.FirstOrDefault().GetLiteral())).ToArray();
                var xpaths = workingPaths.Select(pp => "//" + pathPattern.ReplaceGroup(pp, "separator", xpathSeparator));
                searchParameter.Xpath = String.Join(" | ", xpaths);
            }
            else
            {
                searchParameter.Xpath = String.Empty;
                //Null is not an error, for example Composite parameters don't have a path.
            }
        }

        private static string StripResourceNameFromStart(string path, string resourceName)
        {
            if (path == null || resourceName == null)
            {
                throw new ArgumentException("path and resourceName are both mandatory.");
            }
            if (path.StartsWith(resourceName, StringComparison.CurrentCultureIgnoreCase))
            {
                //Path is like "Patient.birthdate", but "Patient." is superfluous. Ignore it.
                return path.Remove(0, resourceName.Length + 1); 
            }
            else
            {
                return path;
            }
        }

        public static string[] GetPropertyPath(this SearchParameter searchParameter)
        {
            if (searchParameter.Xpath != null)
            {
                var xpaths = searchParameter.Xpath.Split(new string[] { " | " }, StringSplitOptions.None);
                return xpaths.Select(xp => xpathPattern.ReplaceGroups(xp, new Dictionary<string, string>{ { "separator", pathSeparator},{ "root", String.Empty} })).ToArray();
            }
            else
            {
                return new string[] { };
            }
        }

        public static ModelInfo.SearchParamDefinition GetOriginalDefinition(this SearchParameter searchParameter)
        {
            return searchParameter.Annotation<ModelInfo.SearchParamDefinition>();
        }

        public static void SetOriginalDefinition(this SearchParameter searchParameter, ModelInfo.SearchParamDefinition definition)
        {
            searchParameter.AddAnnotation(definition);
        }

        public static SearchParams AddAll(this SearchParams self, List<Tuple<string, string>> @params)
        {
            foreach (var (item1, item2) in @params)
            {
                self.Add(item1, item2);
            }
            return self;
        }
    }
}
