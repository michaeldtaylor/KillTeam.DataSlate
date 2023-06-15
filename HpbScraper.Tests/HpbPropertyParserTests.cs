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
        public void When_parsing_single_property_then_returns_property()
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
        public void When_parsing_no_availability_single_property_then_returns_nothing()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.NoAvailability-SingleProperty-2023_06_15.html")!;

            // Act
            var hpbProperties = HpbPropertyParser.Parse(contentArea, _options);

            // Assert
            hpbProperties.Should().HaveCount(0);
        }

        [Test]
        public void When_parsing_no_availability_on_some_properties_then_returns_available_properties()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.NoAvailability-SomeProperties-2023_06_15.html")!;

            // Act
            var hpbProperties = HpbPropertyParser.Parse(contentArea, _options);

            // Assert
            hpbProperties.Should().HaveCount(1);
            hpbProperties.First().Should().BeEquivalentTo(new HpbProperty("Henllys", "Anglesey, Wales", new Uri(_options.BaseUri, "/Properties/Bond/HL/202308141BED")));
        }

        [Test]
        public void When_parsing_multiple_availability_with_multiple_room_types_then_returns_one_bed_available_properties()
        {
            // Arrange
            var contentArea = typeof(HpbPropertyParserTests).GetRelativeEmbeddedResourceAsString("TestData.MultipleProperties-MultipleRoomTypes-2023_06_15.html")!;

            // Act
            var hpbProperties = HpbPropertyParser.Parse(contentArea, _options);

            // Assert
            hpbProperties.Should().HaveCount(1);
            hpbProperties.First().Should().BeEquivalentTo(new HpbProperty("Henllys", "Anglesey, Wales", new Uri(_options.BaseUri, "/Properties/Bond/HL/202308141BED")));
        }
    }
}
