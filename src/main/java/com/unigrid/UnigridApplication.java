package com.unigrid;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

@SpringBootApplication(scanBasePackages = "com.unigrid")
public class UnigridApplication {

	public static void main(String[] args) {
		SpringApplication.run(UnigridApplication.class, args);
	}

}
