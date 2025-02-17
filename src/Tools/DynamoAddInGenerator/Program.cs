﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Autodesk.RevitAddIns;
using DynamoInstallDetective;
using System.Collections.Generic;

namespace DynamoAddinGenerator
{
    class Program
    {
        private static string debugPath = string.Empty;

        static void Main(string[] args)
        {
            bool uninstall = false;
            foreach (string s in args)
            {
                if (s == @"/uninstall")
                {
                    uninstall = true;
                }
                else if (Directory.Exists(s))
                {
                    debugPath = s;
                }      
            }

            if (uninstall && string.IsNullOrEmpty(debugPath))
            {
                //just use the executing assembly location
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                debugPath = Path.GetDirectoryName(assemblyPath);
            }

            var allProducts = RevitProductUtility.GetAllInstalledRevitProducts();
            var prodCollection = new RevitProductCollection(allProducts.Select(x => new DynamoRevitProduct(x)));
            if (!prodCollection.Products.Any())
            {
                Console.WriteLine("There were no Revit products found.");
                return;
            }

            var dynamos = DynamoProducts.FindDynamoInstallations(debugPath);
            if (!dynamos.Products.Any())
            {
                Console.WriteLine("No Dynamo installation found at {0}.", debugPath);
                DeleteExistingAddins(prodCollection);
                return;
            }

            DeleteExistingAddins(prodCollection);

            if (uninstall)
            {
                GenerateAddins(prodCollection, debugPath);
            }
            else
            {
                GenerateAddins(prodCollection);
            }
        }

        /// <summary>
        /// Deletes all existing Dynamo addins.
        /// This method will delete addins like Dynamo.addin and 
        /// DynamoVersionSelector.addin
        /// </summary>
        /// <param name="products">A collection of revit installs.</param>
        internal static void DeleteExistingAddins(IRevitProductCollection products)
        {
            Console.WriteLine("Deleting all exisitng addins...");
            foreach (var product in products.Products)
            {
                try
                {
                    Console.WriteLine("Checking addins in {0}", product.AddinsFolder);

                    var dynamoAddin = Path.Combine(product.AddinsFolder, "Dynamo.addin");
                    if (File.Exists(dynamoAddin))
                    {
                        Console.WriteLine("Deleting addin {0}", dynamoAddin);
                        File.Delete(dynamoAddin);
                    }

                    dynamoAddin = Path.Combine(product.AddinsFolder, "DynamoRevitVersionSelector.addin");
                    if (File.Exists(dynamoAddin))
                    {
                        Console.WriteLine("Deleting addin {0}", dynamoAddin);
                        File.Delete(dynamoAddin);
                    }

                    dynamoAddin = Path.Combine(product.AddinsFolder, "DynamoVersionSelector.addin");
                    if (File.Exists(dynamoAddin))
                    {
                        Console.WriteLine("Deleting addin {0}", dynamoAddin);
                        File.Delete(dynamoAddin);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("There as an error deleting an addin {0}", product.AddinsFolder);
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }

        /// <summary>
        /// Generate new addin files for all applicable
        /// versions of Revit.
        /// </summary>
        /// <param name="products">A collection of revit installs.</param>
        /// <param name="excludePath">The path that will not be used to search for Dynamo Revit installations</param>
        internal static void GenerateAddins(IRevitProductCollection products, string excludePath = null)
        {
            Console.WriteLine("Generating addins...");
            foreach (var prod in products.Products)
            {
                var subfolder = prod.VersionString.Insert(5, "_");
                Func<string, string> fileLocator =
                    p => Path.Combine(p, subfolder, "DynamoRevitDS.dll");
                var dynRevitProducts = Utilities.LocateDynamoInstallations(null, fileLocator);
                if (null == dynRevitProducts)
                {
                    Console.WriteLine("Dynamo Revit Not Installed!");
                }
                foreach (KeyValuePair<string, Tuple<int, int, int, int>> dynRevitProd in dynRevitProducts)
                {
                    if(dynRevitProd.Key == excludePath)
                    {
                        continue;
                    }
                    var path = Path.Combine(dynRevitProd.Key, subfolder, "DynamoRevitVersionSelector.dll");
                    Console.WriteLine(path);
                    if (File.Exists(path))
                    {
                        var addinData = DynamoAddinData.Create(prod, dynRevitProd.Key);
                        if (null != addinData)
                            GenerateDynamoAddin(addinData);
                    }
                }
            }
        }


        /// <summary>
        /// Generate a Dynamo.addin file.
        /// </summary>
        /// <param name="data">An object containing data about the addin.</param>
        internal static void GenerateDynamoAddin(IDynamoAddinData data)
        {
            Console.WriteLine("Generating addin {0}", data.AddinPath);

            // If Revit has been installed, but not Run, the addins
            // folder will not exist. We need to create it.
            var dir = Path.GetDirectoryName(data.AddinPath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (var tw = new StreamWriter(data.AddinPath, false))
            {
                var addin = String.Format(
                    "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?>\n" +
                    "<RevitAddIns>\n" +
                    "<AddIn Type=\"Application\">\n" +
                    "<Name>Dynamo For Revit</Name>\n" +
                    "<Assembly>\"{0}\"</Assembly>\n" +
                    "<AddInId>{1}</AddInId>\n" +
                    "<FullClassName>{2}</FullClassName>\n" +
                    "<VendorId>ADSK</VendorId>\n" +
                    "<VendorDescription>Dynamo</VendorDescription>\n" +
                    "</AddIn>\n" +
                    "</RevitAddIns>",
                    data.AssemblyPath, data.Id, data.ClassName
                    );

                tw.Write(addin);
                tw.Flush();
            }

            // Grant everyone permissions to delete this addin.
            //http://stackoverflow.com/questions/5298905/add-everyone-privilege-to-folder-using-c-net/5398398#5398398
            var sec = File.GetAccessControl(data.AddinPath);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            sec.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.FullControl, AccessControlType.Allow));
            File.SetAccessControl(data.AddinPath, sec);
        }
    }
}
