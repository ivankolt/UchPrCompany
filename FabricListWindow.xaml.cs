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
                // ИСПРАВЛЕНО: Используем правильные имена столбцов: 'code', 'name', 'conversionfactor'
                var query = "SELECT code, name, conversionfactor FROM public.unitofmeasurement ORDER BY name";
                var unitsData = database.GetData(query);

                units = new List<UnitOfMeasurement>();
                cmbUnit.Items.Add("Все единицы");

                foreach (DataRow row in unitsData.Rows)
                {
                    var unit = new UnitOfMeasurement
                    {
                        Code = Convert.ToInt32(row["code"]),
                        Name = row["name"].ToString(),
                        ConversionFactor = Convert.ToDecimal(row["conversionfactor"])
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
                // ИСПРАВЛЕНО: Используем правильное имя столбца 'name' из таблицы-справочника
                var query = "SELECT id, name FROM public.composition ORDER BY name";
                var compositionsData = database.GetData(query);

                cmbComposition.Items.Add("Все составы");

                foreach (DataRow row in compositionsData.Rows)
                {
                    cmbComposition.Items.Add(row["name"].ToString());
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
                f.article AS ""fabric_article"",
                fn.name AS ""fabric_name"",
                c.name AS ""ColorName"",
                p.name AS ""PatternName"",
                comp.name AS ""CompositionName"",
                COALESCE(fw.quantity, 0) AS ""StockQuantity"",
                COALESCE(fw.total_cost, 0) AS ""TotalCost"",
                CASE 
                    WHEN COALESCE(fw.quantity, 0) > 0 
                    THEN ROUND(COALESCE(fw.total_cost, 0) / fw.quantity, 2)
                    ELSE 0 
                END AS ""AveragePrice"",
                uom.name AS ""AccountingUnitName"",
                COALESCE(f.scrap_threshold, 0) AS ""ScrapLimit"",
                0 AS ""MinStock""
            FROM 
                public.fabric f
            LEFT JOIN 
                public.fabricname fn ON f.name_id = fn.code
            LEFT JOIN 
                public.colors c ON f.color_id = c.id
            LEFT JOIN 
                public.pattern p ON f.pattern_id = p.id
            LEFT JOIN 
                public.composition comp ON f.composition_id = comp.id
            LEFT JOIN 
                public.unitofmeasurement uom ON f.unit_of_measurement_id = uom.code
            LEFT JOIN 
                (SELECT fabric_article, SUM(COALESCE(total_cost, 0)) as total_cost, 
                        SUM(COALESCE(length, 0) * COALESCE(width, 0)) as quantity
                 FROM public.fabricwarehouse 
                 WHERE fabric_article IS NOT NULL
                 GROUP BY fabric_article) fw ON f.article = fw.fabric_article
            ORDER BY 
                f.article";

                fabricsTable = database.GetData(query);

                if (fabricsTable == null)
                {
                    fabricsTable = new DataTable();
                }

                // Отладочная информация
                System.Diagnostics.Debug.WriteLine("Столбцы в fabricsTable:");
                foreach (DataColumn column in fabricsTable.Columns)
                {
                    System.Diagnostics.Debug.WriteLine($"- {column.ColumnName}");
                }

                AddCalculatedColumns();

                if (fabricsTable.Rows.Count > 0)
                {
                    ProcessFabricData();
                }

                dgFabrics.ItemsSource = fabricsTable.DefaultView;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки данных о тканях: {ex.Message}", "Критическая ошибка");
                fabricsTable = new DataTable();
                dgFabrics.ItemsSource = fabricsTable.DefaultView;
            }
        }


        private void AddCalculatedColumns()
        {
            if (fabricsTable == null)
            {
                fabricsTable = new DataTable();
            }

            if (!fabricsTable.Columns.Contains("ConvertedQuantity"))
                fabricsTable.Columns.Add("ConvertedQuantity", typeof(decimal));

            if (!fabricsTable.Columns.Contains("SelectedUnitName"))
                fabricsTable.Columns.Add("SelectedUnitName", typeof(string));

            if (!fabricsTable.Columns.Contains("StatusColor"))
                fabricsTable.Columns.Add("StatusColor", typeof(string));
        }

        private void ProcessFabricData()
        {
            if (fabricsTable == null || fabricsTable.Rows == null)
            {
                return;
            }

            foreach (DataRow row in fabricsTable.Rows)
            {
                try
                {
                    // ИСПРАВЛЕНО: используем правильные имена столбцов
                    decimal stockQuantity = Convert.ToDecimal(row["StockQuantity"] ?? 0);
                    decimal minStock = Convert.ToDecimal(row["MinStock"] ?? 0);
                    decimal scrapLimit = Convert.ToDecimal(row["ScrapLimit"] ?? 0);

                    // Конвертация количества в выбранную единицу
                    if (selectedUnit != null)
                    {
                        row["ConvertedQuantity"] = stockQuantity * selectedUnit.ConversionFactor;
                        row["SelectedUnitName"] = selectedUnit.Name;
                    }
                    else
                    {
                        row["ConvertedQuantity"] = stockQuantity;
                        row["SelectedUnitName"] = row["AccountingUnitName"]?.ToString() ?? "";
                    }

                    // Определение статуса остатка
                    if (stockQuantity <= scrapLimit)
                        row["StatusColor"] = "#FF0000"; // Красный - обрезки
                    else if (stockQuantity <= minStock)
                        row["StatusColor"] = "#FFA500"; // Оранжевый - критический остаток
                    else
                        row["StatusColor"] = "#00FF00"; // Зеленый - нормальный остаток
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка обработки строки: {ex.Message}");
                }
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
        private void ApplyFilters()
        {
            if (fabricsTable == null) return;

            string filter = "";
            var conditions = new List<string>();

            // Фильтр по поиску
            if (!string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                string searchText = txtSearch.Text.Replace("'", "''"); // Экранируем одинарные кавычки
                var searchConditions = new List<string>();

                // Для числового поля fabric_article используем точное совпадение или CONVERT
                if (int.TryParse(searchText, out int articleNumber))
                {
                    searchConditions.Add($"fabric_article = {articleNumber}");
                }

                // Для строкового поля fabric_name используем LIKE
                if (ColumnExists("fabric_name"))
                {
                    searchConditions.Add($"fabric_name LIKE '%{searchText}%'");
                }

                if (searchConditions.Count > 0)
                {
                    conditions.Add($"({string.Join(" OR ", searchConditions)})");
                }
            }

            // Фильтр по составу
            if (cmbComposition.SelectedIndex > 0 && ColumnExists("CompositionName"))
            {
                string selectedComposition = cmbComposition.SelectedItem.ToString().Replace("'", "''");
                conditions.Add($"CompositionName = '{selectedComposition}'");
            }

            if (conditions.Count > 0)
            {
                filter = string.Join(" AND ", conditions);
            }

            try
            {
                fabricsTable.DefaultView.RowFilter = filter;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка применения фильтра: {ex.Message}", "Ошибка фильтрации");
                fabricsTable.DefaultView.RowFilter = ""; // Сбрасываем фильтр при ошибке
            }
        }

        private bool ColumnExists(string columnName)
        {
            return fabricsTable != null && fabricsTable.Columns.Contains(columnName);
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (dgFabrics.SelectedItem == null)
            {
                MessageBox.Show("Выберите ткань для редактирования");
                return;
            }

            var selectedRow = ((DataRowView)dgFabrics.SelectedItem).Row;
            int fabricId = Convert.ToInt32(selectedRow["fabric_article"]); // Теперь это число!

          //  var editWindow = new FabricEditWindow(fabricId);
          //  if (editWindow.ShowDialog() == true)
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
