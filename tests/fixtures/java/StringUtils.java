package com.example.utils;

public class StringUtils {
    public static String toSlug(String input) {
        return input.toLowerCase().replace(" ", "-");
    }
}
