﻿using Microsoft.AspNet.Html.Abstractions;
using Microsoft.Framework.WebEncoders;
using System.IO;

namespace Orchard.DisplayManagement {
    public class PositionWrapper : IHtmlContent, IPositioned {

        private string _value;
        public string Position { get; private set; }

        public PositionWrapper(IHtmlContent value, string position) {
            _value = value.ToString();
            Position = position;
        }

        public PositionWrapper(string value, string position) {
            _value = new HtmlEncoder().HtmlEncode(value);
            Position = position;
        }

        public void WriteTo(TextWriter writer, IHtmlEncoder encoder) {
            writer.Write(_value);
        }
    }
}