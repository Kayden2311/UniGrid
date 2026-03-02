package com.unigrid.dataAccess.entities;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "Admin", schema = "dbo")
public class Admin {
    @Id
    @ColumnDefault("newsequentialid()")
    @Column(name = "adminId", nullable = false)
    private UUID id;

    @Nationalized
    @Column(name = "userName", nullable = false, length = 50)
    private String userName;

    @Nationalized
    @Column(name = "password", nullable = false)
    private String password;

    @Nationalized
    @Column(name = "fullName", length = 100)
    private String fullName;


}