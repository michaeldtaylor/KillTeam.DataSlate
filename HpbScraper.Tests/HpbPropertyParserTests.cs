using System;
using System.Linq;
using FluentAssertions;
using HpbScraper.Domain;
using HpbScraper.Domain.Extensions;
using NUnit.Framework;

namespace HpbScraper.Tests
{
    public class HpbPropertyParserTests
    {
        private readonly HpbOptions _options;

        public HpbPropertyParserTests()
        {
            _options = new HpbOptions();
        }

        [Test]
        public void When_parsing_single_item_then_returns_hpb_property()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.SingleProperty-2023_06_15.html")!;

            // Act
            var hpbProperties = HpbPropertyParser.Parse(contentArea, _options);

            // Assert
            hpbProperties.Should().HaveCount(1);
            hpbProperties.First().Should().BeEquivalentTo(new HpbProperty("Tigh Mor Trossachs", "Trossachs, Scotland", new Uri(_options.BaseUri, "/Properties/Bond/TT/202307171BED")));
        }

        [Test]
        public void When_parsing_no_availability_single_item_then_returns_nothing()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.NoAvailability-SingleProperty-2023_06_15.html")!;

            // Act
            var hpbProperties = HpbPropertyParser.Parse(contentArea, _options);

            // Assert
            hpbProperties.Should().HaveCount(0);
        }
    }
}
