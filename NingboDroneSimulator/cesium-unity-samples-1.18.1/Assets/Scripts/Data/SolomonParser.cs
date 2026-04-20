// Assets/Scripts/Data/SolomonParser.cs
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public static class SolomonParser
{
    // Use numeric char codes to avoid escape sequence corruption during copy-paste
    private static readonly char LF  = (char)10;  // line feed
    private static readonly char CR  = (char)13;  // carriage return
    private static readonly char TAB = (char)9;    // tab
    private static readonly string NEWLINE = ((char)10).ToString();

    public static SolomonDataset ParseRawText(string text, string datasetName = "")
    {
        var dataset = new SolomonDataset { name = datasetName };
        var lines = text.Split(new char[] { LF, CR }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        int i = 0;

        while (i < lines.Count && !lines[i].StartsWith("VEHICLE"))
            i++;

        i++;
        if (i < lines.Count && lines[i].Contains("NUMBER")) i++;

        if (i < lines.Count)
        {
            var parts = SplitNumbers(lines[i]);
            if (parts.Count >= 2)
            {
                dataset.vehicleCount = (int)parts[0];
                dataset.vehicleCapacity = (int)parts[1];
            }
            i++;
        }

        while (i < lines.Count && !lines[i].StartsWith("CUSTOMER"))
            i++;

        i++;
        if (i < lines.Count && lines[i].Contains("CUST")) i++;

        while (i < lines.Count)
        {
            var parts = SplitNumbers(lines[i]);
            if (parts.Count >= 7)
            {
                var customer = new SolomonCustomer
                {
                    id = (int)parts[0],
                    x = (float)parts[1],
                    y = (float)parts[2],
                    demand = (int)parts[3],
                    readyTime = (float)parts[4],
                    dueDate = (float)parts[5],
                    serviceTime = (float)parts[6]
                };

                if (customer.id == 0)
                    dataset.depot = customer;
                else
                    dataset.customers.Add(customer);
            }
            i++;
        }

        Debug.Log("[SolomonParser] Parsed raw text: " + dataset);
        return dataset;
    }

    public static SolomonDataset ParseJson(string json)
    {
        var wrapper = JsonUtility.FromJson<SolomonJsonWrapper>(json);
        if (wrapper == null)
        {
            DLog.Error("General","[SolomonParser] Failed to parse JSON");
            return null;
        }

        var dataset = new SolomonDataset
        {
            name = wrapper.name,
            vehicleCount = wrapper.vehicleCount,
            vehicleCapacity = wrapper.vehicleCapacity
        };

        if (wrapper.mapping != null)
        {
            dataset.mapping = new SolomonDataset.CoordinateMapping
            {
                centerLongitude = wrapper.mapping.centerLongitude,
                centerLatitude = wrapper.mapping.centerLatitude,
                scaleMetersPerUnit = wrapper.mapping.scaleMetersPerUnit,
                flightHeightMeters = wrapper.mapping.flightHeightMeters
            };
        }

        foreach (var c in wrapper.customers)
        {
            var customer = new SolomonCustomer
            {
                id = c.id,
                x = c.x,
                y = c.y,
                demand = c.demand,
                readyTime = c.readyTime,
                dueDate = c.dueDate,
                serviceTime = c.serviceTime
            };

            if (customer.id == 0)
                dataset.depot = customer;
            else
                dataset.customers.Add(customer);
        }

        Debug.Log("[SolomonParser] Parsed JSON: " + dataset);
        return dataset;
    }

    public static SolomonDataset ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            DLog.Error("General","[SolomonParser] File not found: " + filePath);
            return null;
        }

        string text = File.ReadAllText(filePath);
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".json")
        {
            var dataset = ParseJson(text);
            if (dataset != null && dataset.CustomerCount > 0)
                return dataset;
        }

        if (text.Contains("VEHICLE") || text.Contains("CUSTOMER") || text.Contains("CUST NO"))
        {
            return ParseRawText(text, fileName);
        }

        var lines = text.Split(new char[] { LF, CR }, System.StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count > 0 && SplitNumbers(lines[0]).Count >= 7)
        {
            // Build wrapped text using string concat with NEWLINE constant
            string wrapped =
                "VEHICLE" + NEWLINE +
                "NUMBER CAPACITY" + NEWLINE +
                "50 200" + NEWLINE +
                NEWLINE +
                "CUSTOMER" + NEWLINE +
                "CUST NO. XCOORD. YCOORD. DEMAND READY TIME DUE DATE SERVICE TIME" + NEWLINE +
                text;
            return ParseRawText(wrapped, fileName);
        }

        DLog.Error("General","[SolomonParser] Could not determine format of: " + filePath);
        return null;
    }

    public static string ExportToJson(SolomonDataset dataset)
    {
        var wrapper = new SolomonJsonWrapper
        {
            name = dataset.name,
            vehicleCount = dataset.vehicleCount,
            vehicleCapacity = dataset.vehicleCapacity,
            customers = new List<SolomonJsonCustomer>()
        };

        if (dataset.mapping != null)
        {
            wrapper.mapping = new SolomonJsonMapping
            {
                centerLongitude = dataset.mapping.centerLongitude,
                centerLatitude = dataset.mapping.centerLatitude,
                scaleMetersPerUnit = dataset.mapping.scaleMetersPerUnit,
                flightHeightMeters = dataset.mapping.flightHeightMeters
            };
        }

        if (dataset.depot != null)
            wrapper.customers.Add(CustomerToJson(dataset.depot));

        foreach (var c in dataset.customers)
            wrapper.customers.Add(CustomerToJson(c));

        return JsonUtility.ToJson(wrapper, true);
    }

    private static List<float> SplitNumbers(string line)
    {
        var result = new List<float>();
        var parts = line.Split(new char[] { ' ', TAB },
            System.StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (float.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out float val))
                result.Add(val);
        }
        return result;
    }

    private static SolomonJsonCustomer CustomerToJson(SolomonCustomer c)
    {
        return new SolomonJsonCustomer
        {
            id = c.id,
            x = c.x,
            y = c.y,
            demand = c.demand,
            readyTime = c.readyTime,
            dueDate = c.dueDate,
            serviceTime = c.serviceTime
        };
    }

    [System.Serializable]
    private class SolomonJsonWrapper
    {
        public string name;
        public int vehicleCount;
        public int vehicleCapacity;
        public SolomonJsonMapping mapping;
        public List<SolomonJsonCustomer> customers;
    }

    [System.Serializable]
    private class SolomonJsonMapping
    {
        public double centerLongitude;
        public double centerLatitude;
        public double scaleMetersPerUnit;
        public double flightHeightMeters;
    }

    [System.Serializable]
    private class SolomonJsonCustomer
    {
        public int id;
        public float x;
        public float y;
        public int demand;
        public float readyTime;
        public float dueDate;
        public float serviceTime;
    }
}