using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UchPR
{
    public class MaterialReceiptItem
    {
        public string MaterialType { get; set; } 
        public string MaterialName { get; set; }
        public string Article { get; set; }
        public decimal Quantity { get; set; }
        public string UnitName { get; set; }
        public int UnitId { get; set; }
        public decimal UnitPrice { get; set; }

        public decimal TotalSum => Quantity * UnitPrice;
    }
}
