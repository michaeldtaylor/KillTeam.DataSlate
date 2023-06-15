using System;
using System.Collections.Generic;
using System.Linq;
using HtmlAgilityPack;

namespace HpbScraper.Domain
{
    public record HpbProperty(string Name, string Location, Uri Uri);

    public class HpbPropertyParser
    {
        public static List<HpbProperty> Parse(string contentArea, HpbOptions hpbOptions)
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
                var propertyInfoContainerNode = tableNode.GetByElementAndClass( "div", "locDescRow1");

                if (propertyInfoContainerNode == null)
                {
                    continue;
                }

                var noAvailableNode = tableNode.GetByElementAndClass( "h3", "noAvailable");

                if (noAvailableNode != null)
                {
                    continue;
                }

                var propertyUrlContainerNode = tableNode.GetByElementAndClass("td", "viewAll");

                if (propertyUrlContainerNode == null)
                {
                    continue;
                }

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

                // HTML XPath location: /section[1]/header[1]/div[2]/div[2]/#text[1] gives:
                //
                // /Properties/Bond/TT/202307171BED
                var relativeUri = propertyUrlContainerNode.ChildNodes[0].Attributes["href"].Value;

                hpbProperties.Add(new HpbProperty(name, location, new Uri(hpbOptions.BaseUri, relativeUri)));
            }

            return hpbProperties;
        }

        private static void RemoveTablesAfterTenancyProgrammeSeparator(HtmlDocument htmlDocument)
        {
            var separator = GetSeparator(htmlDocument);

            while (separator.NextSibling != null)
            {
                separator.NextSibling.Remove();
                separator = GetSeparator(htmlDocument);
            }
        }

        private static HtmlNode GetSeparator(HtmlDocument htmlDocument)
        {
            return htmlDocument.DocumentNode
                .Descendants("h3")
                .Single(d => d.Attributes.Contains("class") && d.Attributes["class"].Value.Contains("resultsHeader clr"));
        }
    }

    public static class HtmlNodeHelpers
    {
        public static HtmlNode? GetByElementAndClass(this HtmlNode tableNode, string elementName, string className)
        {
            return tableNode
                .Descendants(elementName)
                .SingleOrDefault(d => d.Attributes.Contains("class") && d.Attributes["class"].Value.Contains(className));
        }
    }
}
