using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Torch.Server.ViewModels;
using VRage.Game;
using Xunit;

namespace Torch.Server.Tests
{
    public class TorchServerSessionSettingsTest
    {
        public static PropertyInfo[] ViewModelProperties = typeof(SessionSettingsViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        public static IEnumerable<object[]> ModelFields = typeof(MyObjectBuilder_SessionSettings).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(x => new object[] { x });

        [Theory]
        [MemberData(nameof(ModelFields))]
        public void MissingPropertyTest(FieldInfo modelField)
        {
            // Ignore fields that aren't applicable to SE
            if (modelField.GetCustomAttribute<GameRelationAttribute>()?.RelatedTo == Game.MedievalEngineers)
                return;

            if (string.IsNullOrEmpty(modelField.GetCustomAttribute<DisplayAttribute>()?.Name))
                return;

            var match = ViewModelProperties.FirstOrDefault(p => p.Name.Equals(modelField.Name, StringComparison.InvariantCultureIgnoreCase));

            if (match == null)
                Assert.Fail($"Field '{modelField.Name}' was not present in '{nameof(SessionSettingsViewModel)}'.");
        }
    }
}
