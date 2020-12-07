using System;
using System.Collections.Generic;
using Xunit;
using FizzWare.NBuilder;
using System.Reflection;
using Newtonsoft.Json;

namespace VisualRegressionTracker.Tests
{
    public class DtoTests
    {
        public static IEnumerable<object[]> AllDtoTypes()
        {
            foreach (var type in typeof(VisualRegressionTracker).Assembly.GetTypes())
                if (type.Name.EndsWith("Dto"))
                    yield return new object[] { type };
        }

        [Theory]
        [MemberData(nameof(AllDtoTypes))]
        public void JsonRoundTrip(Type dtoType)
        {
            dynamic dto = this.InvokeGeneric(nameof(BuildDto), new[] { dtoType });
            dto.AdditionalProperties = new Dictionary<string, object> {
                {"a", true},
                {"b", 2}
            };

            if (dtoType == typeof(CreateTestRequestDto))
                dto.IgnoreAreas = new[] {new IgnoreAreaDto{X=1, Y=2, Height=3, Width=4}};

            var json = JsonConvert.SerializeObject(dto);
            dynamic dto2 = JsonConvert.DeserializeObject(json, dtoType);
            var json2 = JsonConvert.SerializeObject(dto2);

            Assert.Equal(json, json2);
            Assert.Equal(dto.AdditionalProperties["a"], dto2.AdditionalProperties["a"]);
            Assert.Equal(dto.AdditionalProperties["b"], dto2.AdditionalProperties["b"]);
        }

        public object BuildDto<DtoType>()
        {
            var dto = Builder<DtoType>.CreateNew().Build();
            return dto;
        }
    }
}