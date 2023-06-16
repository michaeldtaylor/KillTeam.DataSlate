using System;
using System.Linq;
using FluentAssertions;
using HpbScraper.Domain;
using HpbScraper.Domain.Extensions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace HpbScraper.Tests
{
    public class HpbPropertyParserTests
    {
        private readonly HpbPropertyParser _hpbPropertyParser;

        public HpbPropertyParserTests()
        {
            _hpbPropertyParser = new HpbPropertyParser(new OptionsWrapper<HpbScraperOptions>(new HpbScraperOptions
            {
                PropertySize = "One-bed"
            }));
        }

        [Test]
        public void When_parsing_single_property_then_returns_property()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.SingleProperty-2023_06_15.html")!;

            // Act
            var hpbProperties = _hpbPropertyParser.Parse(contentArea);

            // Assert
            hpbProperties.Should().HaveCount(1);
            hpbProperties.First().Should().BeEquivalentTo(new HpbProperty("Tigh Mor Trossachs", "Trossachs, Scotland", new Uri(HpbConstants.BaseUri, "/Properties/Bond/TT/202307171BED")));
        }

        [Test]
        public void When_parsing_no_availability_single_property_then_returns_nothing()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.NoAvailability-SingleProperty-2023_06_15.html")!;

            // Act
            var hpbProperties = _hpbPropertyParser.Parse(contentArea);

            // Assert
            hpbProperties.Should().HaveCount(0);
        }

        [Test]
        public void When_parsing_no_availability_on_some_properties_then_returns_available_properties()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.NoAvailability-SomeProperties-2023_06_15.html")!;

            // Act
            var hpbProperties = _hpbPropertyParser.Parse(contentArea);

            // Assert
            hpbProperties.Should().HaveCount(1);
            hpbProperties.First().Should().BeEquivalentTo(new HpbProperty("Henllys", "Anglesey, Wales", new Uri(HpbConstants.BaseUri, "/Properties/Bond/HL/202308141BED")));
        }

        [Test]
        public void When_parsing_multiple_availability_with_multiple_room_types_then_returns_one_bed_available_properties()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.MultipleProperties-MultipleRoomTypes-2023_06_15.html")!;

            // Act
            var hpbProperties = _hpbPropertyParser.Parse(contentArea);

            var expectedHpbProperties = new []
            {
                new HpbProperty("Henllys", "Anglesey, Wales", new Uri(HpbConstants.BaseUri, "/Properties/Bond/HL/202310091BED")),
                new HpbProperty("Lodge Yard, Askrigg", "Yorkshire Dales, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/LY/202310091BED")),
                new HpbProperty("St Brides Castle", "Pembrokeshire, Wales", new Uri(HpbConstants.BaseUri, "/Properties/Bond/SB/202310091BED")),
            };

            // Assert
            hpbProperties.Should().HaveCount(3);
            hpbProperties.Should().BeEquivalentTo(expectedHpbProperties);
        }

        [Test]
        public void When_parsing_multiple_availability_with_no_one_bed_option_then_returns_one_bed_available_properties()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.MultipleProperties-NoOneBedOption-2023_06_15.html")!;

            // Act
            var hpbProperties = _hpbPropertyParser.Parse(contentArea);

            var expectedHpbProperties = new[]
            {
                new HpbProperty("Braithwaite Court", "Cumbria, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/BC/202311061BED")),
                new HpbProperty("Henllys", "Anglesey, Wales", new Uri(HpbConstants.BaseUri, "/Properties/Bond/HL/202311061BED")),
                new HpbProperty("Lodge Yard, Askrigg", "Yorkshire Dales, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/LY/202311061BED")),
                new HpbProperty("Merlewood", "Cumbria, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/MD/202311061BED")),
                new HpbProperty("Sibton Park", "Kent, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/SK/202311061BED")),
                new HpbProperty("St Brides Castle", "Pembrokeshire, Wales", new Uri(HpbConstants.BaseUri, "/Properties/Bond/SB/202311061BED")),
            };

            // Assert
            hpbProperties.Should().HaveCount(6);
            hpbProperties.Should().BeEquivalentTo(expectedHpbProperties);
        }

        [Test]
        public void When_parsing_multiple_availability_with_no_options_then_returns_one_bed_available_properties()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.MultipleProperties-NoOptions-2023_06_16.html")!;

            // Act
            var hpbProperties = _hpbPropertyParser.Parse(contentArea);

            var expectedHpbProperties = new[]
            {
                new HpbProperty("Blore Hall", "Peak District, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/BL/202506161BED")),
                new HpbProperty("Coo Palace", "Kirkcudbright, Scotland", new Uri(HpbConstants.BaseUri, "/Properties/Bond/OO/202506161BED")),
                new HpbProperty("Henllys", "Anglesey, Wales", new Uri(HpbConstants.BaseUri, "/Properties/Bond/HL/202506161BED")),
                new HpbProperty("Langton House", "Dorset, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/LH/202506161BED")),
                new HpbProperty("Lucker Hall", "Northumberland, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/LU/202506161BED")),
                new HpbProperty("Northway Court", "Devon, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/NW/202506161BED")),
                new HpbProperty("St Brides Castle", "Pembrokeshire, Wales", new Uri(HpbConstants.BaseUri, "/Properties/Bond/SB/202506161BED")),
                new HpbProperty("Upper Norton", "Shropshire, England", new Uri(HpbConstants.BaseUri, "/Properties/Bond/NH/202506161BED")),
                new HpbProperty("Witch's Pool", "Perthshire, Scotland", new Uri(HpbConstants.BaseUri, "/Properties/Bond/WP/202506161BED")),
            };

            // Assert
            hpbProperties.Should().HaveCount(9);
            hpbProperties.Should().BeEquivalentTo(expectedHpbProperties);
        }
    }
}
