using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Npgsql;

namespace UchPR
{
    public partial class FabricListWindow : Window
    {
        private DataBase database;
        private DataTable fabricsTable;
        private string currentUserRole;
        private List<UnitOfMeasurement> units;
        private UnitOfMeasurement selectedUnit;

        public FabricListWindow(string userRole)
        {
            InitializeComponent();
            database = new DataBase();
            currentUserRole = userRole;

            // Проверка прав доступа
            if (currentUserRole != "Кладовщик")
            {
                MessageBox.Show("У вас нет прав для доступа к этой форме", "Ошибка доступа",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                this.Close();
                return;
            }

            LoadUnits();
            LoadCompositions();
            LoadFabrics();
        }

        private void LoadUnits()
        {
            try
            {
                var query = "SELECT unitid as Code, unitname as Name, COALESCE(conversionfactor, 1) as ConversionFactor FROM unitofmeasurement ORDER BY unitname";
                var unitsData = database.GetData(query);

                units = new List<UnitOfMeasurement>();
                cmbUnit.Items.Add("Все единицы");

                foreach (DataRow row in unitsData.Rows)
                {
                    var unit = new UnitOfMeasurement
                    {
                        Code = Convert.ToInt32(row["Code"]),
                        Name = row["Name"].ToString(),
                        ConversionFactor = Convert.ToDecimal(row["ConversionFactor"])
                    };
                    units.Add(unit);
                    cmbUnit.Items.Add(unit.Name);
                }

                cmbUnit.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки единиц измерения: {ex.Message}");
            }
        }

        private void LoadCompositions()
        {
            try
            {
                var query = "SELECT * FROM composition ORDER BY compositionname";
                var compositionsData = database.GetData(query);

                cmbComposition.Items.Add("Все составы");

                foreach (DataRow row in compositionsData.Rows)
                {
                    cmbComposition.Items.Add(row["compositionname"].ToString());
                }

                cmbComposition.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки составов: {ex.Message}");
            }
        }

        private void LoadFabrics()
        {
            try
            {
                string query = @"
                    SELECT 
                        f.fabricid,
                        f.fabricarticlenum,
                        f.fabricname,
                        c.colorname as ColorName,
                        p.patternname as PatternName,
                        comp.compositionname as CompositionName,
                        COALESCE(fw.quantity, 0) as StockQuantity,
                        COALESCE(fw.totalcost, 0) as TotalCost,
                        CASE 
                            WHEN COALESCE(fw.quantity, 0) > 0 
                            THEN COALESCE(fw.totalcost, 0) / fw.quantity 
                            ELSE 0 
                        END as AveragePrice,
                        u.unitname as AccountingUnitName,
                        COALESCE(f.minstock, 0) as MinStock,
                        COALESCE(f.scraplimit, 0) as ScrapLimit
                    FROM fabric f
                    LEFT JOIN fabricwarehouse fw ON f.fabricid = fw.fabricid
                    LEFT JOIN colors c ON f.fabriccolorid = c.colorid
                    LEFT JOIN pattern p ON f.fabricpatternid = p.patternid
                    LEFT JOIN composition comp ON f.fabriccompositionid = comp.compositionid
                    LEFT JOIN unitofmeasurement u ON f.accountingunitid = u.unitid
                    ORDER BY f.fabricarticlenum";

                fabricsTable = database.GetData(query);

                // Добавляем вычисляемые поля
                AddCalculatedColumns();
                ProcessFabricData();

                dgFabrics.ItemsSource = fabricsTable.DefaultView;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных о тканях: {ex.Message}");
            }
        }

        private void AddCalculatedColumns()
        {
            if (!fabricsTable.Columns.Contains("ConvertedQuantity"))
                fabricsTable.Columns.Add("ConvertedQuantity", typeof(decimal));

            if (!fabricsTable.Columns.Contains("SelectedUnitName"))
                fabricsTable.Columns.Add("SelectedUnitName", typeof(string));

            if (!fabricsTable.Columns.Contains("StatusColor"))
                fabricsTable.Columns.Add("StatusColor", typeof(string));
        }

        private void ProcessFabricData()
        {
            foreach (DataRow row in fabricsTable.Rows)
            {
                decimal stockQuantity = Convert.ToDecimal(row["StockQuantity"]);
                decimal minStock = Convert.ToDecimal(row["MinStock"]);
                decimal scrapLimit = Convert.ToDecimal(row["ScrapLimit"]);

                // Конвертация количества в выбранную единицу
                if (selectedUnit != null)
                {
                    row["ConvertedQuantity"] = stockQuantity * selectedUnit.ConversionFactor;
                    row["SelectedUnitName"] = selectedUnit.Name;
                }
                else
                {
                    row["ConvertedQuantity"] = stockQuantity;
                    row["SelectedUnitName"] = row["AccountingUnitName"];
                }

                // Определение статуса остатка
                if (stockQuantity <= scrapLimit)
                    row["StatusColor"] = "#FF0000"; // Красный - обрезки
                else if (stockQuantity <= minStock)
                    row["StatusColor"] = "#FFA500"; // Оранжевый - критический остаток
                else
                    row["StatusColor"] = "#00FF00"; // Зеленый - нормальный остаток
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CmbUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbUnit.SelectedIndex > 0)
            {
                selectedUnit = units[cmbUnit.SelectedIndex - 1];
            }
            else
            {
                selectedUnit = null;
            }

            ProcessFabricData();
            ApplyFilters();
        }

        private void CmbComposition_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (fabricsTable == null) return;

            string filter = "";
            var conditions = new List<string>();

            // Фильтр по поиску
            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                conditions.Add($"(fabricarticlenum LIKE '%{txtSearch.Text}%' OR fabricname LIKE '%{txtSearch.Text}%')");
            }

            // Фильтр по составу
            if (cmbComposition.SelectedIndex > 0)
            {
                conditions.Add($"CompositionName = '{cmbComposition.SelectedItem}'");
            }

            if (conditions.Count > 0)
            {
                filter = string.Join(" AND ", conditions);
            }

            fabricsTable.DefaultView.RowFilter = filter;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadFabrics();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // Открываем форму добавления новой ткани
          //  var addWindow = new FabricEditWindow();
          //  if (addWindow.ShowDialog() == true)
            {
                LoadFabrics();
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (dgFabrics.SelectedItem == null)
            {
                MessageBox.Show("Выберите ткань для редактирования");
                return;
            }

            var selectedRow = ((DataRowView)dgFabrics.SelectedItem).Row;
            int fabricId = Convert.ToInt32(selectedRow["fabricid"]);

           // var editWindow = new FabricEditWindow(fabricId);
           // if (editWindow.ShowDialog() == true)
            {
                LoadFabrics();
            }
        }

        private void BtnReceive_Click(object sender, RoutedEventArgs e)
        {
            if (dgFabrics.SelectedItem == null)
            {
                MessageBox.Show("Выберите ткань для оформления поступления");
                return;
            }

            var selectedRow = ((DataRowView)dgFabrics.SelectedItem).Row;
            int fabricId = Convert.ToInt32(selectedRow["fabricid"]);

            var receiptWindow = new MaterialReceiptWindow("fabric", fabricId);
            if (receiptWindow.ShowDialog() == true)
            {
                LoadFabrics();
            }
        }

        private void BtnScrap_Click(object sender, RoutedEventArgs e)
        {
            ProcessScrapFabrics();
        }

        private void ProcessScrapFabrics()
        {
            try
            {
                var scrapItems = new List<string>();
                decimal totalScrapCost = 0;

                foreach (DataRow row in fabricsTable.Rows)
                {
                    decimal stockQuantity = Convert.ToDecimal(row["StockQuantity"]);
                    decimal scrapLimit = Convert.ToDecimal(row["ScrapLimit"]);

                    if (stockQuantity > 0 && stockQuantity <= scrapLimit)
                    {
                        decimal itemCost = Convert.ToDecimal(row["TotalCost"]);
                        scrapItems.Add($"{row["fabricname"]}: {stockQuantity} {row["AccountingUnitName"]} на сумму {itemCost:F2} руб.");
                        totalScrapCost += itemCost;

                        // Списываем обрезки
                        ScrapFabric(Convert.ToInt32(row["fabricid"]), stockQuantity, itemCost);
                    }
                }

                if (scrapItems.Count > 0)
                {
                    string message = $"Списано обрезков на общую сумму {totalScrapCost:F2} руб.:\n\n";
                    message += string.Join("\n", scrapItems);
                    MessageBox.Show(message, "Списание обрезков");
                    LoadFabrics();
                }
                else
                {
                    MessageBox.Show("Нет тканей для списания в обрезки");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при списании обрезков: {ex.Message}");
            }
        }

        private void ScrapFabric(int fabricId, decimal quantity, decimal cost)
        {
            string query = @"
                INSERT INTO scraplog (materialtype, materialid, quantity, cost, scrapdate, reason)
                VALUES ('fabric', @fabricId, @quantity, @cost, @scrapDate, 'Автоматическое списание обрезков');
                
                UPDATE fabricwarehouse 
                SET quantity = 0, totalcost = 0 
                WHERE fabricid = @fabricId";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@fabricId", fabricId),
                new NpgsqlParameter("@quantity", quantity),
                new NpgsqlParameter("@cost", cost),
                new NpgsqlParameter("@scrapDate", DateTime.Now)
            };

            database.ExecuteQuery(query, parameters);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
         //   var settingsWindow = new FabricSettingsWindow();
           // if (settingsWindow.ShowDialog() == true)
            {
                LoadFabrics();
            }
        }
    }

}
