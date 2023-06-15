using System;
using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;

namespace HpbScraper.Domain
{
    public record HpbProperty(string Name, string Location, Uri Uri);

    public class HpbPropertyParser
    {
        public static List<HpbProperty> Parse(string contentArea, HpbOptions hpbOptions, string propertyType = "One-bed")
        {
            if (string.IsNullOrEmpty(contentArea))
            {
                throw new ArgumentNullException(nameof(contentArea));
            }

            if (hpbOptions == null)
            {
                throw new ArgumentNullException(nameof(hpbOptions));
            }

            var htmlDocument = new HtmlDocument();

            // This turns the string into an object model (i.e. a collection of hierarchical nodes).
            htmlDocument.LoadHtml(contentArea);

            RemoveTablesAfterTenancyProgrammeSeparator(htmlDocument);

            // This gets all the root 'table' nodes in the 'HTML' object model.
            var tableNodes = htmlDocument.DocumentNode
                .Descendants("table")
                .Where(x => x.ParentNode == htmlDocument.DocumentNode);

            if (tableNodes == null)
            {
                throw new InvalidOperationException("There was an error getting any 'table' nodes from the HTML list. The HTML structure has probably changed!");
            }

            var hpbProperties = new List<HpbProperty>();

            // In 'HpbScraper.Tests\TestData\SingleProperty-2023_06_15' you will see that a 'table' looks like this:

            // The structure without the noise:
            //
            // <table>
            //     <tbody>
            //         <tr>...</tr>
            //         <tr>
            //             <td class="locDesc">
            //                 <div class="locDescRow1">
            //                     <img src="/Images/Locations/Bond/TT.jpg">
            //                     <h3>Tigh Mor Trossachs(Bond)</h3>
            //                     <h5>Trossachs, Scotland</h5>
            //                 </div>
            //             </td>
            //             <td>
            //                 <table class="propTypeDetails">
            //                     <tbody>
            //                         <tr>
            //                             <td class="viewAll"
            //                                 <a href="/Properties/Bond/TT/202307171BED">View all</a>
            //                             </td>
            //                         </tr>
            //                     </tbody>
            //                 </table>
            //             </td>
            //         </tr>
            //     </tbody>
            // </table>
            //
            // <table>
            // ...
            // </table>
            //
            // // Ignore all tables below this point
            // <h3 class="resultsHeader clr">We also have 18 locations within our HPB Tenancy programme that match your criteria</h3>
            //
            // For now, we want to get some data out of the 'header' child nodes. In the future 'footer' might be useful.
            //
            // HTML XPath location: /table[1]/tr[2]/td[1]/div[1] gives:
            //
            // <div class="locDescRow1">
            //     <img src="/Images/Locations/Bond/TT.jpg">
            //     <h3>Tigh Mor Trossachs(Bond)</h3>
            //     <h5>Trossachs, Scotland</h5>
            // </div>
            //
            // HTML XPath location: /table[1]/tr[2]/table[1]/tbody[1]/tr[1]/td[1] gives:
            //
            // <a href="/Properties/Bond/TT/202307171BED">View all</a>
            //
            // Note: XPath is 1 indexed. C# is 0 indexed.
            foreach (var tableNode in tableNodes)
            {
                var trNodes = GetTrNodes(tableNode, htmlDocument.DocumentNode);

                var skipNext2Rows = false;
                var skipCount = 0;

                foreach (var trNode in trNodes)
                {
                    if (skipNext2Rows)
                    {
                        if (skipCount == 2)
                        {
                            skipNext2Rows = false;
                            skipCount = 0;
                        }

                        skipCount++;

                        continue;
                    }

                    var noAvailableNode = trNode.GetByElementAndClass("h3", "noAvailable");

                    if (noAvailableNode != null)
                    {
                        skipNext2Rows = true;
                        continue;
                    }

                    var propertyInfoContainerNode = trNode.GetByElementAndClass("div", "locDescRow1");

                    if (propertyInfoContainerNode == null)
                    {
                        continue;
                    }

                    var propertyTypeTableNode = trNode.GetByElementAndClass("table", "propTypeDetails");

                    if (propertyTypeTableNode == null)
                    {
                        continue;
                    }

                    var propertyTypeTrNodes = GetTrNodes(propertyTypeTableNode, trNode);
                    var propertyTypeTrNode = propertyTypeTrNodes.Single(p => p.ChildNodes[1].InnerText == propertyType);
                    var viewAllTdNode = propertyTypeTrNode.GetByElementAndClass("td", "viewAll");

                    // Turn a collection of nodes, into a collection of stock items.
                    // HTML XPath location: /h3[1]/#text[1] gives:
                    //
                    // Tigh Mor Trossachs (Bond)
                    var name = propertyInfoContainerNode.ChildNodes[1].InnerText.Replace(" (Bond)", string.Empty);

                    if (hpbOptions.NamesToExclude.Contains(name))
                    {
                        continue;
                    }

                    // HTML XPath location: /h5[1]/#text[1] gives:
                    //
                    // Trossachs, Scotland
                    var location = propertyInfoContainerNode.ChildNodes[2].InnerText;

                    // HTML XPath location: /table[1]/tr[14] gives:
                    //
                    // /Properties/Bond/TT/202307171BED
                    var relativeUri = viewAllTdNode!.ChildNodes[0].Attributes["href"].Value;

                    hpbProperties.Add(new HpbProperty(name, location, new Uri(hpbOptions.BaseUri, relativeUri)));
                }
            }

            return hpbProperties;
        }

        private static IEnumerable<HtmlNode> GetTrNodes(HtmlNode tableNode, HtmlNode parentNode)
        {
            // var tbodyNode = tableNode.ChildNodes[1];
            //var trNodes = htmlDocument.DocumentNode
            //    .Descendants("tr")
            //    .Where(x => x.ParentNode == tbodyNode);

            var tbodyNode = tableNode.ChildNodes[1];
            var trNodes = parentNode
                .Descendants("tr")
                .Where(x => x.ParentNode == tbodyNode);

            return trNodes;
        }

        private static void RemoveTablesAfterTenancyProgrammeSeparator(HtmlDocument htmlDocument)
        {
            var separator = GetSeparator(htmlDocument);

            while (separator?.NextSibling != null)
            {
                separator.NextSibling.Remove();
                separator = GetSeparator(htmlDocument);
            }
        }

        private static HtmlNode? GetSeparator(HtmlDocument htmlDocument)
        {
            return htmlDocument.DocumentNode.GetByElementAndClass("h3", "resultsHeader clr");
        }
    }

    public static class HtmlNodeHelpers
    {
        public static HtmlNode? GetByElementAndClass(this HtmlNode htmlNode, string elementName, string className)
        {
            return htmlNode
                .Descendants(elementName)
                .SingleOrDefault(d => d.Attributes.Contains("class") && d.Attributes["class"].Value.Contains(className));
        }
    }
}
