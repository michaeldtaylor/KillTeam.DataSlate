using System;
using System.Collections.Generic;

namespace HpbScraper.Domain;

public record HpbProperty(string Name, string Location, Uri Uri);

public record HpbPropertyGroup(string DateRange, List<HpbProperty> HpbProperties);
